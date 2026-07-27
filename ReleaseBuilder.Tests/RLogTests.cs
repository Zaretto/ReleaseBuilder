using ReleaseBuilder;
using Xunit;

namespace ReleaseBuilder.Tests
{
    public class RLogTests : IDisposable
    {
        public RLogTests()
        {
            RLog.ResetErrorCount();
            RLog.ResetSecrets();
        }

        public void Dispose() => RLog.ResetSecrets();

        [Fact]
        public void ErrorFormat_increments_ErrorCount()
        {
            RLog.ErrorFormat("test error");
            Assert.Equal(1, RLog.ErrorCount);
        }

        [Fact]
        public void FatalFormat_increments_ErrorCount()
        {
            RLog.FatalFormat("test fatal");
            Assert.Equal(1, RLog.ErrorCount);
        }

        [Fact]
        public void InfoFormat_does_not_increment_ErrorCount()
        {
            RLog.InfoFormat("test info");
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void DebugFormat_does_not_increment_ErrorCount()
        {
            RLog.DebugFormat("test debug");
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void ResetErrorCount_clears_count()
        {
            RLog.ErrorFormat("error 1");
            RLog.ErrorFormat("error 2");
            Assert.Equal(2, RLog.ErrorCount);
            RLog.ResetErrorCount();
            Assert.Equal(0, RLog.ErrorCount);
        }

        [Fact]
        public void Multiple_errors_accumulate()
        {
            RLog.ErrorFormat("error 1");
            RLog.ErrorFormat("error 2");
            RLog.ErrorFormat("error 3");
            Assert.Equal(3, RLog.ErrorCount);
        }

        // --- secret redaction ---

        [Fact]
        public void Registered_secret_is_redacted()
        {
            RLog.RegisterSecret("s3cr3t-api-key-value");
            Assert.Equal("API_KEY=***", RLog.Redact("API_KEY=s3cr3t-api-key-value"));
        }

        [Fact]
        public void Secret_is_redacted_wherever_it_appears()
        {
            RLog.RegisterSecret("s3cr3t-api-key-value");
            Assert.Equal("[dry-run] Would exec: push --api-key *** --source ***",
                RLog.Redact("[dry-run] Would exec: push --api-key s3cr3t-api-key-value --source s3cr3t-api-key-value"));
        }

        [Fact]
        public void Unregistered_value_is_untouched()
        {
            RLog.RegisterSecret("s3cr3t-api-key-value");
            Assert.Equal("nothing to hide here", RLog.Redact("nothing to hide here"));
        }

        [Fact]
        public void Short_values_are_not_registered()
        {
            // Redacting a short value would blank out unrelated text all over the log.
            RLog.RegisterSecret("1234567");
            Assert.Equal("PIN=1234567", RLog.Redact("PIN=1234567"));
        }

        [Fact]
        public void Minimum_length_value_is_registered()
        {
            RLog.RegisterSecret("12345678");
            Assert.Equal("PIN=***", RLog.Redact("PIN=12345678"));
        }

        [Fact]
        public void Null_and_empty_are_ignored()
        {
            RLog.RegisterSecret(null);
            RLog.RegisterSecret("");
            Assert.Equal("unchanged text", RLog.Redact("unchanged text"));
        }

        [Fact]
        public void Multiple_secrets_are_all_redacted()
        {
            RLog.RegisterSecret("first-secret-value");
            RLog.RegisterSecret("second-secret-value");
            Assert.Equal("a=*** b=***", RLog.Redact("a=first-secret-value b=second-secret-value"));
        }

        [Fact]
        public void Longer_secret_containing_another_is_redacted_whole()
        {
            RLog.RegisterSecret("token-abcdefgh");
            RLog.RegisterSecret("token-abcdefgh-extended");
            Assert.Equal("***", RLog.Redact("token-abcdefgh-extended"));
        }

        [Fact]
        public void Registering_the_same_secret_twice_is_harmless()
        {
            RLog.RegisterSecret("repeated-secret-value");
            RLog.RegisterSecret("repeated-secret-value");
            Assert.Equal("x=***", RLog.Redact("x=repeated-secret-value"));
        }

        [Fact]
        public void ResetSecrets_clears_registrations()
        {
            RLog.RegisterSecret("temporary-secret-value");
            RLog.ResetSecrets();
            Assert.Equal("x=temporary-secret-value", RLog.Redact("x=temporary-secret-value"));
        }

        [Fact]
        public void GitHub_Actions_is_asked_to_mask_the_secret()
        {
            var original = Console.Out;
            var wasSet = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
            var captured = new StringWriter();
            try
            {
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
                Console.SetOut(captured);
                RLog.RegisterSecret("ci-masked-secret-value");
            }
            finally
            {
                Console.SetOut(original);
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", wasSet);
            }
            Assert.Contains("::add-mask::ci-masked-secret-value", captured.ToString());
        }

        [Fact]
        public void No_mask_command_outside_GitHub_Actions()
        {
            var original = Console.Out;
            var wasSet = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
            var captured = new StringWriter();
            try
            {
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
                Console.SetOut(captured);
                RLog.RegisterSecret("local-secret-value");
            }
            finally
            {
                Console.SetOut(original);
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", wasSet);
            }
            Assert.DoesNotContain("::add-mask::", captured.ToString());
        }

        [Fact]
        public void Logged_output_is_redacted()
        {
            var text = CaptureConsole(() =>
            {
                RLog.Level = LogMessageLevel.Debug;
                RLog.RegisterSecret("logged-secret-value");
                RLog.DebugFormat("{0}={1}", "API_KEY", "logged-secret-value");
            });
            Assert.DoesNotContain("logged-secret-value", text);
            Assert.Contains("API_KEY=***", text);
        }

        /// <summary>
        /// Captures console output with GITHUB_ACTIONS cleared: the ::add-mask:: workflow
        /// command has to carry the raw value, so the test would otherwise find the secret
        /// in its own capture when run on a CI runner.
        /// </summary>
        internal static string CaptureConsole(Action action)
        {
            var original = Console.Out;
            var wasSet = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
            var level = RLog.Level;
            var captured = new StringWriter();
            try
            {
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", null);
                Console.SetOut(captured);
                action();
            }
            finally
            {
                Console.SetOut(original);
                Environment.SetEnvironmentVariable("GITHUB_ACTIONS", wasSet);
                RLog.Level = level;
            }
            return captured.ToString();
        }
    }
}
