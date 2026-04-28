using System.Collections;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml.XPath;
using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// Tests for the xml-edit action, in particular the attribute case which uses
    /// XPathEvaluate so that predicates like @*[local-name()='x'] work correctly.
    /// </summary>
    public class XmlEditTests : IDisposable
    {
        private readonly string _tempDir;

        public XmlEditTests()
        {
            RLog.ResetErrorCount();
            _tempDir = Path.Combine(AppContext.BaseDirectory, "test_xmledit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // Verify the runtime behaviour of XPathEvaluate before relying on it in higher-level tests.
        [Fact]
        public void XPathEvaluate_returns_IEnumerable_object_for_attribute_xpath()
        {
            // XPathEvaluate returns IEnumerable<object>, not IEnumerable<XObject>.
            // Items are XAttribute/XElement at runtime; the pattern match must use object.
            var xdoc = XDocument.Parse("""<root versionName="0.0.0" versionCode="1" />""");
            var result = xdoc.XPathEvaluate("/root/@versionName");

            Assert.IsAssignableFrom<IEnumerable<object>>(result);
            var items = ((IEnumerable<object>)result).ToList();
            Assert.Single(items);
            var attr = Assert.IsType<XAttribute>(items[0]);
            Assert.Equal("0.0.0", attr.Value);

            // Verify mutation propagates back to the document.
            attr.Value = "9.9.9";
            Assert.Equal("9.9.9", xdoc.Root!.Attribute("versionName")!.Value);
        }

        // Minimal helper: run xml-edit on a document via a ReleaseConfig that wraps the action.
        private XDocument RunXmlEdit(string targetXml, string editXml)
        {
            var targetFile = Path.Combine(_tempDir, "target.xml");
            File.WriteAllText(targetFile, targetXml);

            // Use raw backslash-escaped path so PathFinder handles it on all platforms.
            var escapedPath = targetFile.Replace("\\", "/");
            var config = $"""
                <?xml version="1.0"?>
                <ReleaseConfig>
                  <Target name="Release" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <xml-edit file="{escapedPath}">
                        {editXml}
                      </xml-edit>
                    </build>
                  </Artefacts>
                </ReleaseConfig>
                """;

            var configFile = Path.Combine(_tempDir, "ReleaseConfig.xml");
            File.WriteAllText(configFile, config);

            var rb = new ReleaseBuilder(
                new DirectoryInfo(_tempDir),
                new FileInfo(configFile),
                "Release",
                Enumerable.Empty<DirectoryInfo>(),
                nobuild: false,
                useShellExecute: false,
                dryRun: false);

            rb.Build();
            return XDocument.Load(targetFile);
        }

        [Fact]
        public void Node_case_updates_element_value()
        {
            var result = RunXmlEdit(
                """<root><version>0.0.0</version></root>""",
                """<node path="/root/version" action="set,1.2.3" />""");

            Assert.Equal("1.2.3", result.XPathSelectElement("/root/version")?.Value);
        }

        [Fact]
        public void Attribute_case_updates_named_attribute()
        {
            var result = RunXmlEdit(
                """<root versionName="0.0.0" versionCode="1" />""",
                """<attribute path="/root/@versionName" action="set,2.0.0" />""");

            Assert.Equal("2.0.0", result.Root?.Attribute("versionName")?.Value);
            Assert.Equal("1", result.Root?.Attribute("versionCode")?.Value);
        }

        [Fact]
        public void Attribute_case_with_local_name_predicate_updates_attribute()
        {
            // Simulates the Android manifest pattern where the attribute has a namespace
            // prefix that LINQ to XML can't resolve without a namespace manager.
            // The local-name() predicate is the correct workaround.
            var result = RunXmlEdit(
                """<root versionName="0.0.0" versionCode="1" />""",
                """<attribute path="/root/@*[local-name()='versionName']" action="set,3.0.0" />""");

            Assert.Equal("3.0.0", result.Root?.Attribute("versionName")?.Value);
        }

        [Fact]
        public void Attribute_case_non_matching_path_logs_no_error()
        {
            RunXmlEdit(
                """<root versionName="0.0.0" />""",
                """<attribute path="/root/@*[local-name()='nonexistent']" action="set,x" />""");

            Assert.Equal(0, RLog.ErrorCount);
        }
    }
}
