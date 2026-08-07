using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// Guards against the schema drifting from what the code actually accepts. Nothing validated
    /// configs against ReleaseConfig.xsd before, which is how the xml-edit &lt;attribute&gt; child
    /// came to be supported in code but missing from the schema.
    /// </summary>
    public class SchemaValidationTests
    {
        /// <summary>Walks up from the test output directory to the repository root.</summary>
        private static DirectoryInfo RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ReleaseConfig.xsd")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!;
        }

        private static XmlSchemaSet LoadSchema(DirectoryInfo root)
        {
            var set = new XmlSchemaSet();
            set.Add(null, Path.Combine(root.FullName, "ReleaseConfig.xsd"));
            set.Compile();
            return set;
        }

        [Fact]
        public void Schema_compiles()
        {
            var set = LoadSchema(RepositoryRoot());
            Assert.True(set.IsCompiled);
        }

        [Fact]
        public void Repository_config_validates_against_the_schema()
        {
            var root = RepositoryRoot();
            Validate(LoadSchema(root), File.ReadAllText(Path.Combine(root.FullName, "ReleaseConfig.xml")));
        }

        [Fact]
        public void Certificate_actions_validate_against_the_schema()
        {
            Validate(LoadSchema(RepositoryRoot()), """
                <?xml version="1.0"?>
                <ReleaseConfig>
                  <Target name="live" type="folder" path="." />
                  <Artefacts>
                    <build>
                      <create-certificate file="authserver.pfx" subject="CN=AuthServer" purpose="token-signing"
                                          password="$AUTH_PFX_PASSWORD" days="3650" key="rsa" key-size="3072"
                                          hash="sha256" overwrite="true" prefix="AUTH" />
                      <create-certificate file="leaf.pfx" subject="CN=Leaf" eku="code-signing,1.3.6.1.5.5.7.3.8"
                                          key-usage="digital-signature,key-encipherment" issuer-file="root.pfx"
                                          issuer-password="$CA_PASSWORD" dns-names="a.test,b.test"
                                          not-before="2026-01-01T00:00:00Z" active="when,~OS~,==,windows" />
                      <check-certificate file="signing.pfx" password="$PFX_PASSWORD" min-days="30"
                                         private-key="true" purpose="code-signing" key-algorithm="rsa"
                                         min-key-size="2048" trusted="true" revocation="false"
                                         thumbprint="AABBCC" subject="Acme" prefix="SIGN" on-fail="warn"
                                         active="when,~OS~,==,windows">
                        <trust-root file="root.cer" />
                        <trust-root file="root2.pfx" password="$CA_PASSWORD" />
                      </check-certificate>
                      <exec app="signtool" args="sign" active="when,~SIGN_VALID~,==,1" />
                    </build>
                  </Artefacts>
                </ReleaseConfig>
                """);
        }

        [Fact]
        public void Xml_edit_attribute_child_validates_against_the_schema()
        {
            Validate(LoadSchema(RepositoryRoot()), """
                <?xml version="1.0"?>
                <ReleaseConfig>
                  <Artefacts>
                    <build>
                      <xml-edit file="app.config">
                        <node path="/configuration/x" action="set,1" />
                        <attribute path="/configuration/x/@version" action="set,2" />
                      </xml-edit>
                    </build>
                  </Artefacts>
                </ReleaseConfig>
                """);
        }

        [Fact]
        public void Unknown_purpose_is_rejected_by_the_schema()
        {
            var ex = Assert.Throws<XmlSchemaValidationException>(() => Validate(LoadSchema(RepositoryRoot()), """
                <?xml version="1.0"?>
                <ReleaseConfig>
                  <Artefacts>
                    <build>
                      <create-certificate file="x.pfx" subject="CN=X" purpose="nonsense" />
                    </build>
                  </Artefacts>
                </ReleaseConfig>
                """));
            Assert.Contains("purpose", ex.Message);
        }

        private static void Validate(XmlSchemaSet schemas, string xml)
        {
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = schemas,
            };
            settings.ValidationEventHandler += (_, e) => throw e.Exception;

            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read()) { }
        }
    }
}
