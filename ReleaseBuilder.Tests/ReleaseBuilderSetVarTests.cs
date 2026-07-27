using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// Verifies that &lt;Set&gt; child elements on &lt;ReleaseBuilder&gt; inject variables
    /// into the child config's variable store, making them available as ~VAR~ references
    /// throughout the child build.
    /// </summary>
    public class ReleaseBuilderSetVarTests : IDisposable
    {
        private readonly string _tempDir;

        public ReleaseBuilderSetVarTests()
        {
            RLog.ResetErrorCount();
            RLog.ResetSecrets();
            // Temp dirs live inside the test output directory so GitVersion can find
            // the repository root and return a valid version.
            _tempDir = Path.Combine(AppContext.BaseDirectory, "test_setvars_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            RLog.ResetSecrets();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        /// <summary>
        /// Runs the build with debug logging captured, so tests can assert on what reached the log.
        /// </summary>
        private static string CaptureBuildLog(ReleaseBuilder rb)
        {
            var original = Console.Out;
            var captured = new StringWriter();
            var level = RLog.Level;
            try
            {
                Console.SetOut(captured);
                RLog.Level = LogMessageLevel.Debug;
                rb.Build();
            }
            finally
            {
                Console.SetOut(original);
                RLog.Level = level;
            }
            return captured.ToString();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private ReleaseBuilder BuildParent(string parentConfig)
        {
            var configFile = new FileInfo(Path.Combine(_tempDir, "ReleaseConfig.xml"));
            File.WriteAllText(configFile.FullName, parentConfig);
            return new ReleaseBuilder(
                new DirectoryInfo(_tempDir), configFile, "live",
                Enumerable.Empty<DirectoryInfo>(),
                nobuild: false, useShellExecute: false, dryRun: false);
        }

        private string ChildDir(string name)
        {
            var dir = Path.Combine(_tempDir, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Xml(string body) =>
            $"<?xml version=\"1.0\"?>\n<ReleaseConfig>\n{body}\n</ReleaseConfig>";

        // ------------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------------

        [Fact]
        public void Single_Set_variable_is_available_in_child_create()
        {
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~INJECTED~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false">
                    <Set name="INJECTED" value="hello_from_parent" />
                  </ReleaseBuilder>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("hello_from_parent", File.ReadAllText(Path.Combine(childDir, "output.txt")));
        }

        [Fact]
        public void Multiple_Set_elements_all_reach_child()
        {
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~VAR_A~|~VAR_B~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false">
                    <Set name="VAR_A" value="alpha" />
                    <Set name="VAR_B" value="beta" />
                  </ReleaseBuilder>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("alpha|beta", File.ReadAllText(Path.Combine(childDir, "output.txt")));
        }

        [Fact]
        public void Set_value_is_expanded_using_parent_variables_before_injection()
        {
            // Parent has a Target-Set variable; the ReleaseBuilder Set value references it.
            // The child should receive the already-expanded value.
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~FULL_PATH~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path=".">
                    <Set name="BASE" value="Release" />
                  </Target>
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false">
                    <Set name="FULL_PATH" value="~BASE~\bin" />
                  </ReleaseBuilder>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            // ~BASE~ is expanded by the parent before the value reaches the child
            Assert.Equal(@"Release\bin", File.ReadAllText(Path.Combine(childDir, "output.txt")));
        }

        [Fact]
        public void Injected_var_gates_active_condition_correctly()
        {
            // Child has two arch-conditional Artefacts blocks; only the one matching
            // the injected DEVKIT_ARCH should fire.
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts active="when,~DEVKIT_ARCH~,==,x86">
                    <build>
                      <create file="x86.txt">x86_fired</create>
                    </build>
                  </Artefacts>
                  <Artefacts active="when,~DEVKIT_ARCH~,==,x64">
                    <build>
                      <create file="x64.txt">x64_fired</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false">
                    <Set name="DEVKIT_ARCH" value="x86" />
                  </ReleaseBuilder>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.True(File.Exists(Path.Combine(childDir, "x86.txt")), "x86 block should have fired");
            Assert.False(File.Exists(Path.Combine(childDir, "x64.txt")), "x64 block should have been skipped");
        }

        [Fact]
        public void Different_Set_vars_injected_for_two_sibling_children()
        {
            // Demonstrates the x86/x64 devkit scenario: same child config file,
            // two <ReleaseBuilder> elements each injecting different vars.
            var childConfig = Path.Combine(_tempDir, "ReleaseConfigDevkit.xml");
            File.WriteAllText(childConfig, Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="arch_~DEVKIT_ARCH~.txt">~BUILD_DIR~</create>
                    </build>
                  </Artefacts>
                """));

            // Use the parent's own dir as "folder" (no subfolder needed)
            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{_tempDir.Replace("\\", "/")}"
                                  file="ReleaseConfigDevkit.xml" process="false">
                    <Set name="BUILD_DIR"   value="..\Release" />
                    <Set name="DEVKIT_ARCH" value="x86" />
                  </ReleaseBuilder>
                  <ReleaseBuilder folder="{_tempDir.Replace("\\", "/")}"
                                  file="ReleaseConfigDevkit.xml" process="false">
                    <Set name="BUILD_DIR"   value="..\Release64" />
                    <Set name="DEVKIT_ARCH" value="x64" />
                  </ReleaseBuilder>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal(@"..\Release",   File.ReadAllText(Path.Combine(_tempDir, "arch_x86.txt")));
            Assert.Equal(@"..\Release64", File.ReadAllText(Path.Combine(_tempDir, "arch_x64.txt")));
        }

        [Fact]
        public void Parent_Target_Set_vars_are_inherited_by_child_without_explicit_Set()
        {
            // Parent has <Target><Set name="ENV" value="staging"/>. Child should see
            // ~ENV~ without any <Set> on <ReleaseBuilder>.
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~ENV~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path=".">
                    <Set name="ENV" value="staging" />
                  </Target>
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false" />
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("staging", File.ReadAllText(Path.Combine(childDir, "output.txt")));
        }

        [Fact]
        public void Set_on_ReleaseBuilder_overrides_inherited_parent_var()
        {
            // Parent sets ENV=staging via Target Set; the <ReleaseBuilder> <Set>
            // overrides it to production for that specific child invocation.
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~ENV~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path=".">
                    <Set name="ENV" value="staging" />
                  </Target>
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false">
                    <Set name="ENV" value="production" />
                  </ReleaseBuilder>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("production", File.ReadAllText(Path.Combine(childDir, "output.txt")));
        }

        [Fact]
        public void Child_PUBLISHROOT_is_childs_own_directory_not_parents()
        {
            // PUBLISHROOT is a built-in re-derived from the child's own root directory.
            // It must not be overwritten by the inherited parent value.
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~PUBLISHROOT~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false" />
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            var written = File.ReadAllText(Path.Combine(childDir, "output.txt"));
            // Child's PUBLISHROOT must point to childDir, not the parent temp dir.
            Assert.Equal(childDir, written, ignoreCase: true);
        }

        [Fact]
        public void Target_Set_transform_generates_a_random_key()
        {
            // <Set transform="random-key,24"/> puts a generated secret in a variable that
            // is then usable throughout the build.
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="API_SECRET" transform="random-key,24" />
                  </Target>
                  <Artefacts>
                    <build>
                      <create file="secret.txt">~API_SECRET~</create>
                    </build>
                  </Artefacts>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            var key = File.ReadAllText(Path.Combine(_tempDir, "secret.txt"));
            Assert.Equal(24, key.Length);
            Assert.All(key, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"unexpected character '{c}'"));
        }

        [Fact]
        public void Same_generated_key_is_used_everywhere_in_the_build()
        {
            // The key is generated once when the variable is set, not per reference.
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="API_SECRET" transform="random-key" />
                  </Target>
                  <Artefacts>
                    <build>
                      <create file="first.txt">~API_SECRET~</create>
                      <create file="second.txt">~API_SECRET~</create>
                    </build>
                  </Artefacts>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            var first = File.ReadAllText(Path.Combine(_tempDir, "first.txt"));
            Assert.Equal(32, first.Length);
            Assert.Equal(first, File.ReadAllText(Path.Combine(_tempDir, "second.txt")));
        }

        [Fact]
        public void Target_Set_value_expands_variables()
        {
            // Target <Set> values are expanded like every other attribute, so built-in and
            // earlier user variables can be referenced.
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="BASE"   value="Release" />
                    <Set name="OUTPUT" value="~BASE~/~TYPE~" />
                  </Target>
                  <Artefacts>
                    <build>
                      <create file="output.txt">~OUTPUT~</create>
                    </build>
                  </Artefacts>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("Release/live", File.ReadAllText(Path.Combine(_tempDir, "output.txt")));
        }

        [Fact]
        public void Target_Set_value_keeps_unpaired_tilde_literal()
        {
            // A lone ~ (a home-relative path, say) is not a variable reference.
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="DEST" value="~/deploy/app" />
                  </Target>
                  <Artefacts>
                    <build>
                      <create file="dest.txt">~DEST~</create>
                    </build>
                  </Artefacts>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("~/deploy/app", File.ReadAllText(Path.Combine(_tempDir, "dest.txt")));
        }

        [Fact]
        public void Target_Set_transform_applies_to_expanded_value()
        {
            // With a transform present the value attribute is expanded first, so the
            // transform operates on the resolved variable.
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="BASE" value="1.2.3" />
                    <Set name="UNDERSCORED" value="~BASE~" transform="replace,.,_" />
                  </Target>
                  <Artefacts>
                    <build>
                      <create file="value.txt">~UNDERSCORED~</create>
                    </build>
                  </Artefacts>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("1_2_3", File.ReadAllText(Path.Combine(_tempDir, "value.txt")));
        }

        [Fact]
        public void Generated_key_injected_into_child_build()
        {
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~CHILD_SECRET~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false">
                    <Set name="CHILD_SECRET" transform="random-key,16,hex" />
                  </ReleaseBuilder>
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            var key = File.ReadAllText(Path.Combine(childDir, "output.txt"));
            Assert.Equal(16, key.Length);
            Assert.All(key, c => Assert.Contains(c, "0123456789abcdef"));
        }

        [Fact]
        public void Secret_Set_value_is_redacted_in_the_log()
        {
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="API_KEY" value="s3cr3t-portal-value" secret="true" />
                  </Target>
                  <Artefacts>
                    <build>
                      <create file="out.txt">~API_KEY~</create>
                    </build>
                  </Artefacts>
                """));

            var log = CaptureBuildLog(rb);

            Assert.Equal(0, RLog.ErrorCount);
            Assert.DoesNotContain("s3cr3t-portal-value", log);
            Assert.Contains("API_KEY=***", log);
            // The artefact itself still receives the real value.
            Assert.Equal("s3cr3t-portal-value", File.ReadAllText(Path.Combine(_tempDir, "out.txt")));
        }

        [Fact]
        public void Non_secret_Set_value_is_still_logged()
        {
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="ENVIRONMENT" value="production-eu-west" />
                  </Target>
                """));

            var log = CaptureBuildLog(rb);

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Contains("ENVIRONMENT=production-eu-west", log);
        }

        [Fact]
        public void Generated_key_is_redacted_without_the_secret_attribute()
        {
            // random-key output registers itself: it exists to be a secret.
            var rb = BuildParent(Xml("""
                  <Target name="live" type="folder" path=".">
                    <Set name="MACHINE_KEY" transform="random-key,32" />
                  </Target>
                  <Artefacts>
                    <build>
                      <create file="key.txt">~MACHINE_KEY~</create>
                    </build>
                  </Artefacts>
                """));

            var log = CaptureBuildLog(rb);

            Assert.Equal(0, RLog.ErrorCount);
            var key = File.ReadAllText(Path.Combine(_tempDir, "key.txt"));
            Assert.Equal(32, key.Length);
            Assert.DoesNotContain(key, log);
            Assert.Contains("MACHINE_KEY=***", log);
        }

        [Fact]
        public void Secret_injected_into_child_build_is_redacted_there_too()
        {
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="output.txt">~CHILD_SECRET~</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false">
                    <Set name="CHILD_SECRET" value="child-secret-value" secret="true" />
                  </ReleaseBuilder>
                """));

            var log = CaptureBuildLog(rb);

            Assert.Equal(0, RLog.ErrorCount);
            Assert.DoesNotContain("child-secret-value", log);
            Assert.Equal("child-secret-value", File.ReadAllText(Path.Combine(childDir, "output.txt")));
        }

        [Fact]
        public void ReleaseBuilder_without_Set_children_still_works()
        {
            // Backward-compatibility: existing <ReleaseBuilder> elements with no <Set>
            // children must continue to work without error.
            var childDir = ChildDir("child");
            File.WriteAllText(Path.Combine(childDir, "ReleaseConfig.xml"), Xml("""
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create file="ok.txt">ok</create>
                    </build>
                  </Artefacts>
                """));

            var rb = BuildParent(Xml($"""
                  <Target name="live" type="folder" path="." />
                  <ReleaseBuilder folder="{childDir.Replace("\\", "/")}" process="false" />
                """));

            rb.Build();

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("ok", File.ReadAllText(Path.Combine(childDir, "ok.txt")));
        }
    }
}
