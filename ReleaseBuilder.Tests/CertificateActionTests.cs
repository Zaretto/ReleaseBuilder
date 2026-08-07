using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// End-to-end coverage of the &lt;check-certificate&gt; and &lt;create-certificate&gt; build
    /// actions, driven through a real config. Certificates are generated in-process so the tests
    /// carry no fixture binaries and run on every platform.
    /// </summary>
    public class CertificateActionTests : IDisposable
    {
        private readonly string _tempDir;

        public CertificateActionTests()
        {
            RLog.ResetErrorCount();
            RLog.ResetSecrets();
            // Inside the test output directory so GitVersion can find the repository root.
            _tempDir = Path.Combine(AppContext.BaseDirectory, "test_certaction_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            RLog.ResetSecrets();
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static string Xml(string body) =>
            $"<?xml version=\"1.0\"?>\n<ReleaseConfig>\n  <Target name=\"live\" type=\"folder\" path=\".\" />\n  <Artefacts>\n    <build>\n{body}\n    </build>\n  </Artefacts>\n</ReleaseConfig>";

        private ReleaseBuilder Builder(string body, bool dryRun = false)
        {
            var configFile = new FileInfo(Path.Combine(_tempDir, "ReleaseConfig.xml"));
            File.WriteAllText(configFile.FullName, Xml(body));
            return new ReleaseBuilder(
                new DirectoryInfo(_tempDir), configFile, "live",
                Enumerable.Empty<DirectoryInfo>(),
                nobuild: false, useShellExecute: false, dryRun: dryRun);
        }

        private static string CaptureBuildLog(ReleaseBuilder rb) =>
            RLogTests.CaptureConsole(() =>
            {
                RLog.Level = LogMessageLevel.Debug;
                rb.Build();
            });

        /// <summary>Writes a PFX into the temp directory and returns its file name.</summary>
        private string WritePfx(string name, string subject = "CN=Test", int days = 90, int startOffsetDays = -1,
            string? password = null, IReadOnlyList<string>? ekus = null)
        {
            var (cert, pfx) = CertificateGenerator.Create(new CertificateGenerateOptions
            {
                Subject = subject,
                KeySize = 2048,
                NotBefore = DateTimeOffset.UtcNow.AddDays(startOffsetDays),
                Days = days,
                Password = password,
                Ekus = ekus ?? Array.Empty<string>(),
            });
            cert.Dispose();
            File.WriteAllBytes(Path.Combine(_tempDir, name), pfx);
            return name;
        }

        private string ReadOutput(string name) => File.ReadAllText(Path.Combine(_tempDir, name));

        // ------------------------------------------------------------------
        // check-certificate
        // ------------------------------------------------------------------

        [Fact]
        public void Valid_certificate_produces_no_errors()
        {
            var pfx = WritePfx("signing.pfx");
            var rb = Builder($"""      <check-certificate file="{pfx}" />""");
            Assert.True(rb.Build());
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void Expired_certificate_fails_the_build_with_the_config_line_number()
        {
            var pfx = WritePfx("expired.pfx", days: 1, startOffsetDays: -400);
            var rb = Builder($"""      <check-certificate file="{pfx}" />""");
            var log = CaptureBuildLog(rb);

            Assert.Equal(1, RLog.ErrorCount);
            Assert.Contains("expired", log);
            Assert.Contains("Line:", log);
        }

        [Fact]
        public void On_fail_warn_downgrades_the_failure_and_still_exports_the_verdict()
        {
            var pfx = WritePfx("expired.pfx", days: 1, startOffsetDays: -400);
            var rb = Builder($"""
                  <check-certificate file="{pfx}" on-fail="warn" />
                  <create file="verdict.txt">~CERT_VALID~</create>
            """);
            Assert.True(rb.Build());
            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("0", ReadOutput("verdict.txt"));
        }

        [Fact]
        public void Missing_certificate_fails_the_build()
        {
            var rb = Builder("""      <check-certificate file="nowhere.pfx" />""");
            rb.Build();
            Assert.Equal(1, RLog.ErrorCount);
        }

        [Fact]
        public void Min_days_failure_is_reported()
        {
            var pfx = WritePfx("soon.pfx", days: 6);
            var rb = Builder($"""      <check-certificate file="{pfx}" min-days="30" />""");
            var log = CaptureBuildLog(rb);
            Assert.Equal(1, RLog.ErrorCount);
            Assert.Contains("min-days", log);
        }

        [Fact]
        public void Exported_variables_are_available_to_later_actions()
        {
            var pfx = WritePfx("signing.pfx", subject: "CN=Acme Signing");
            var rb = Builder($"""
                  <check-certificate file="{pfx}" />
                  <create file="vars.txt">~CERT_VALID~|~CERT_SUBJECT~|~CERT_KEY_ALGORITHM~|~CERT_KEY_SIZE~|~CERT_HAS_PRIVATE_KEY~|~CERT_EKU~</create>
            """);
            Assert.True(rb.Build());

            var parts = ReadOutput("vars.txt").Split('|');
            Assert.Equal("1", parts[0]);
            Assert.Contains("CN=Acme Signing", parts[1]);
            Assert.Equal("rsa", parts[2]);
            Assert.Equal("2048", parts[3]);
            Assert.Equal("1", parts[4]);
            Assert.Equal("(none)", parts[5]);
        }

        [Fact]
        public void Prefix_renames_the_exported_variables()
        {
            var pfx = WritePfx("signing.pfx");
            var rb = Builder($"""
                  <check-certificate file="{pfx}" prefix="SIGN" />
                  <create file="vars.txt">~SIGN_VALID~|~SIGN_THUMBPRINT~</create>
            """);
            Assert.True(rb.Build());

            var parts = ReadOutput("vars.txt").Split('|');
            Assert.Equal("1", parts[0]);
            Assert.Equal(40, parts[1].Length);      // SHA-1 thumbprint
        }

        [Fact]
        public void Purpose_against_a_certificate_with_no_eku_warns_but_does_not_fail()
        {
            var pfx = WritePfx("signing.pfx");
            var rb = Builder($"""      <check-certificate file="{pfx}" purpose="code-signing" />""");
            var log = CaptureBuildLog(rb);

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Contains("unconstrained", log);
        }

        [Fact]
        public void Purpose_against_a_certificate_with_the_wrong_eku_fails()
        {
            var pfx = WritePfx("server.pfx", ekus: new[] { CertificatePurpose.ServerAuthOid });
            var rb = Builder($"""      <check-certificate file="{pfx}" purpose="code-signing" />""");
            rb.Build();
            Assert.Equal(1, RLog.ErrorCount);
        }

        [Fact]
        public void Unknown_purpose_is_a_clean_error()
        {
            var pfx = WritePfx("signing.pfx");
            var rb = Builder($"""      <check-certificate file="{pfx}" purpose="nonsense" />""");
            var log = CaptureBuildLog(rb);
            Assert.Equal(1, RLog.ErrorCount);
            Assert.Contains("unknown purpose", log);
        }

        // ------------------------------------------------------------------
        // Secrets
        // ------------------------------------------------------------------

        [Fact]
        public void Password_attribute_is_redacted_from_the_log()
        {
            const string password = "pfx-password-1234";
            var pfx = WritePfx("signing.pfx", password: password);
            var rb = Builder($"""      <check-certificate file="{pfx}" password="{password}" />""");

            var log = CaptureBuildLog(rb);
            Assert.Equal(0, RLog.ErrorCount);
            Assert.DoesNotContain(password, log);
        }

        [Fact]
        public void Password_from_an_environment_variable_is_redacted_from_the_log()
        {
            const string password = "env-pfx-password-1234";
            var previous = Environment.GetEnvironmentVariable("RB_TEST_PFX_PASSWORD");
            try
            {
                Environment.SetEnvironmentVariable("RB_TEST_PFX_PASSWORD", password);
                var pfx = WritePfx("signing.pfx", password: password);
                var rb = Builder($"""      <check-certificate file="{pfx}" password="$RB_TEST_PFX_PASSWORD" />""");

                var log = CaptureBuildLog(rb);
                Assert.Equal(0, RLog.ErrorCount);
                Assert.DoesNotContain(password, log);
            }
            finally
            {
                Environment.SetEnvironmentVariable("RB_TEST_PFX_PASSWORD", previous);
            }
        }

        // ------------------------------------------------------------------
        // create-certificate
        // ------------------------------------------------------------------

        [Fact]
        public void Created_certificate_passes_a_check_in_the_same_config()
        {
            var rb = Builder("""
                  <create-certificate file="authserver.pfx" subject="CN=AuthServer" purpose="token-signing" days="3650" key-size="2048" />
                  <check-certificate file="authserver.pfx" min-days="3000" />
            """);
            Assert.True(rb.Build());
            Assert.Equal(0, RLog.ErrorCount);
            Assert.True(File.Exists(Path.Combine(_tempDir, "authserver.pfx")));
        }

        [Fact]
        public void Token_signing_certificate_carries_no_eku_and_emits_no_trust_warning()
        {
            var rb = Builder("""
                  <create-certificate file="authserver.pfx" subject="CN=AuthServer" key-size="2048" />
                  <create file="vars.txt">~CERT_EKU~|~CERT_HAS_PRIVATE_KEY~|~CERT_VALID~</create>
            """);
            var log = CaptureBuildLog(rb);

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Equal("(none)|1|1", ReadOutput("vars.txt"));
            Assert.DoesNotContain("will not be trusted", log);
        }

        [Fact]
        public void Self_signed_code_signing_certificate_warns_that_it_is_not_trusted()
        {
            var rb = Builder("""      <create-certificate file="test-signing.pfx" subject="CN=Test Signing" purpose="code-signing" key-size="2048" />""");
            var log = CaptureBuildLog(rb);

            Assert.Equal(0, RLog.ErrorCount);
            Assert.Contains("will not be trusted", log);
        }

        [Fact]
        public void Overwrite_defaults_to_refusing_to_replace_an_existing_file()
        {
            var path = Path.Combine(_tempDir, "existing.pfx");
            File.WriteAllText(path, "original");

            var rb = Builder("""      <create-certificate file="existing.pfx" subject="CN=Test" key-size="2048" />""");
            var log = CaptureBuildLog(rb);

            Assert.Equal(1, RLog.ErrorCount);
            Assert.Contains("already exists", log);
            Assert.Equal("original", File.ReadAllText(path));
        }

        [Fact]
        public void Overwrite_true_replaces_an_existing_file()
        {
            var path = Path.Combine(_tempDir, "existing.pfx");
            File.WriteAllText(path, "original");

            var rb = Builder("""      <create-certificate file="existing.pfx" subject="CN=Test" key-size="2048" overwrite="true" />""");
            Assert.True(rb.Build());
            Assert.NotEqual("original", File.ReadAllText(path));
        }

        [Fact]
        public void Eku_attribute_overrides_the_purpose_preset()
        {
            var rb = Builder("""
                  <create-certificate file="ts.pfx" subject="CN=Test" eku="time-stamping,code-signing" key-size="2048" />
                  <create file="vars.txt">~CERT_EKU~</create>
            """);
            Assert.True(rb.Build());
            Assert.Equal("time-stamping,code-signing", ReadOutput("vars.txt"));
        }

        [Fact]
        public void Ca_signed_leaf_chains_to_the_generated_root()
        {
            var rb = Builder("""
                  <create-certificate file="root.pfx" subject="CN=Test Root" purpose="ca" days="1000" key-size="2048" />
                  <create-certificate file="leaf.pfx" subject="CN=Test Leaf" purpose="code-signing" issuer-file="root.pfx" key-size="2048" />
                  <check-certificate file="leaf.pfx" trusted="true" purpose="code-signing">
                    <trust-root file="root.pfx" />
                  </check-certificate>
            """);
            var log = CaptureBuildLog(rb);
            Assert.Equal(0, RLog.ErrorCount);
            Assert.DoesNotContain("not trusted", log);
        }

        [Fact]
        public void Leaf_without_its_root_in_the_trust_store_fails_the_chain_check()
        {
            var rb = Builder("""
                  <create-certificate file="root.pfx" subject="CN=Test Root" purpose="ca" days="1000" key-size="2048" />
                  <create-certificate file="leaf.pfx" subject="CN=Test Leaf" issuer-file="root.pfx" key-size="2048" />
                  <check-certificate file="leaf.pfx" trusted="true" />
            """);
            var log = CaptureBuildLog(rb);
            Assert.Equal(1, RLog.ErrorCount);
            Assert.Contains("chain is not trusted", log);
        }

        [Theory]
        [InlineData("""key="dsa" """)]
        [InlineData("""hash="md5" """)]
        [InlineData("""key-size="1024" """)]
        [InlineData("""days="0" """)]
        [InlineData("""not-before="not-a-date" """)]
        [InlineData("""eku="nonsense" """)]
        [InlineData("""key-usage="nonsense" """)]
        public void Bad_attribute_values_are_logged_errors_not_exceptions(string attribute)
        {
            var rb = Builder($"""      <create-certificate file="bad.pfx" subject="CN=Test" {attribute}/>""");
            rb.Build();
            Assert.Equal(1, RLog.ErrorCount);
            Assert.False(File.Exists(Path.Combine(_tempDir, "bad.pfx")));
        }

        // ------------------------------------------------------------------
        // Dry run
        // ------------------------------------------------------------------

        [Fact]
        public void Dry_run_skips_the_write_but_still_publishes_the_variables()
        {
            var rb = Builder("""
                  <create-certificate file="dry.pfx" subject="CN=Dry Run" key-size="2048" />
                  <create file="vars.txt">~CERT_THUMBPRINT~</create>
            """, dryRun: true);
            Assert.True(rb.Build());

            Assert.False(File.Exists(Path.Combine(_tempDir, "dry.pfx")));
            // <create> is skipped under dry-run too, so the proof that the variable resolved is
            // that expanding it did not throw "Variable not found".
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void Dry_run_still_reports_an_expired_certificate()
        {
            var pfx = WritePfx("expired.pfx", days: 1, startOffsetDays: -400);
            var rb = Builder($"""      <check-certificate file="{pfx}" />""", dryRun: true);
            rb.Build();
            Assert.Equal(1, RLog.ErrorCount);
        }

        // ------------------------------------------------------------------
        // active= on build actions
        // ------------------------------------------------------------------

        [Fact]
        public void Non_matching_active_skips_the_action()
        {
            var pfx = WritePfx("expired.pfx", days: 1, startOffsetDays: -400);
            var rb = Builder($"""      <check-certificate file="{pfx}" active="when,~OS~,==,not-a-real-os" />""");
            Assert.True(rb.Build());
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void Matching_active_runs_the_action()
        {
            var (os, _, _) = ReleaseBuilder.GetPlatformInfo();
            var pfx = WritePfx("expired.pfx", days: 1, startOffsetDays: -400);
            var rb = Builder($"""      <check-certificate file="{pfx}" active="when,~OS~,==,{os}" />""");
            rb.Build();
            Assert.Equal(1, RLog.ErrorCount);
        }

        /// <summary>active= was previously ignored on build actions; it now applies to all of them.</summary>
        [Fact]
        public void Active_is_honoured_on_pre_existing_actions_too()
        {
            var rb = Builder("""
                  <create file="skipped.txt" active="when,~OS~,==,not-a-real-os">nope</create>
                  <create file="written.txt">yes</create>
            """);
            Assert.True(rb.Build());
            Assert.False(File.Exists(Path.Combine(_tempDir, "skipped.txt")));
            Assert.Equal("yes", ReadOutput("written.txt"));
        }
    }
}
