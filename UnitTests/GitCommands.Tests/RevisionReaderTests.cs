﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using ApprovalTests.Reporters.ContinuousIntegration;
using FluentAssertions;
using GitCommands;
using GitExtUtils;
using GitUIPluginInterfaces;
using Newtonsoft.Json;
using NUnit.Framework;

namespace GitCommandsTests
{
    [TestFixture]
    public sealed class RevisionReaderTests
    {
        private Encoding _logOutputEncoding = Encoding.UTF8;
        private long _sixMonths = new DateTimeOffset(new DateTime(2021, 01, 01)).ToUnixTimeSeconds();

        [SetUp]
        public void Setup()
        {
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        public void BuildArguments_should_add_maxcount_if_requested(int maxCount, bool expected)
        {
            RevisionReader reader = RevisionReader.TestAccessor.RevisionReader(new GitModule(""), hasReflogSelector: false, _logOutputEncoding, _sixMonths);
            ArgumentBuilder args = reader.BuildArguments(maxCount, RefFilterOptions.All, "", "", "", out bool parentsAreRewritten);

            if (expected)
            {
                args.ToString().Should().Contain($" --max-count={maxCount} ");
            }
            else
            {
                args.ToString().Should().NotContain(" --max-count=");
            }

            parentsAreRewritten.Should().BeFalse();
        }

        [Test]
        public void BuildArguments_should_be_NUL_terminated()
        {
            RevisionReader reader = RevisionReader.TestAccessor.RevisionReader(new GitModule(""), hasReflogSelector: false, _logOutputEncoding, _sixMonths);
            ArgumentBuilder args = reader.BuildArguments(-1, RefFilterOptions.All, "", "", "", out bool parentsAreRewritten);

            args.ToString().Should().Contain(" log -z ");
            parentsAreRewritten.Should().BeFalse();
        }

        [TestCase(RefFilterOptions.FirstParent, false)]
        [TestCase(RefFilterOptions.FirstParent | RefFilterOptions.Reflogs, true)]
        [TestCase(RefFilterOptions.Branches, false)]
        [TestCase(RefFilterOptions.Branches | RefFilterOptions.Reflogs, true)]
        [TestCase(RefFilterOptions.All, false)]
        [TestCase(RefFilterOptions.All | RefFilterOptions.Reflogs, true)]
        public void BuildArguments_should_add_reflog_if_requested(RefFilterOptions refFilterOptions, bool expected)
        {
            RevisionReader reader = RevisionReader.TestAccessor.RevisionReader(new GitModule(""), hasReflogSelector: false, _logOutputEncoding, _sixMonths);
            ArgumentBuilder args = reader.BuildArguments(-1, refFilterOptions, "", "", "", out bool parentsAreRewritten);

            if (expected)
            {
                args.ToString().Should().Contain(" --reflog");
            }
            else
            {
                args.ToString().Should().NotContain(" --reflog");
            }

            parentsAreRewritten.Should().BeFalse();
        }

        /* first 'parent first' */
        [TestCase(RefFilterOptions.FirstParent, " --first-parent ", null)]
        [TestCase(RefFilterOptions.FirstParent | RefFilterOptions.NoMerges, " --first-parent ", null)]
        [TestCase(RefFilterOptions.All, null, " --first-parent ")]
        /* if not 'first parent', then 'all' */
        [TestCase(RefFilterOptions.FirstParent, null, " --all ")]
        [TestCase(RefFilterOptions.FirstParent | RefFilterOptions.All, " --first-parent ", null)]
        [TestCase(RefFilterOptions.All, " --all ", null)]
        /* if not 'first parent' and not 'all' - selected branches, if requested */
        [TestCase(RefFilterOptions.FirstParent | RefFilterOptions.Branches, " --first-parent ", null)]
        [TestCase(RefFilterOptions.All | RefFilterOptions.Branches, " --all ", " --branches=")]
        [TestCase(RefFilterOptions.Branches, " --branches=", null)]
        /* Disable special refs with --all */
        [TestCase(RefFilterOptions.All | RefFilterOptions.NoStash, " --all ", null)]
        [TestCase(RefFilterOptions.All | RefFilterOptions.NoStash, " --exclude=refs/stash ", null)]
        [TestCase(RefFilterOptions.NoStash, null, " --exclude=refs/stash")]
        [TestCase(RefFilterOptions.All | RefFilterOptions.NoGitNotes, " --all ", null)]
        [TestCase(RefFilterOptions.All | RefFilterOptions.NoGitNotes, " --not --glob=notes --not ", null)]
        [TestCase(RefFilterOptions.NoGitNotes, null, " --not --glob=notes --not ")]
        public void BuildArguments_check_parameters(RefFilterOptions refFilterOptions, string expectedToContain, string notExpectedToContain)
        {
            RevisionReader reader = RevisionReader.TestAccessor.RevisionReader(new GitModule(""), hasReflogSelector: false, _logOutputEncoding, _sixMonths);
            ArgumentBuilder args = reader.BuildArguments(-1, refFilterOptions, "my_*", "my_revision", "my_path", out bool parentsAreRewritten);

            if (expectedToContain is not null)
            {
                args.ToString().Should().Contain(expectedToContain);
            }

            if (notExpectedToContain is not null)
            {
                args.ToString().Should().NotContain(notExpectedToContain);
            }

            parentsAreRewritten.Should().BeTrue();
        }

        [Test]
        public void TryParseRevisionshould_return_false_if_argument_is_invalid()
        {
            ArraySegment<byte> chunk = null;
            RevisionReader reader = RevisionReader.TestAccessor.RevisionReader(new(""), hasReflogSelector: false, _logOutputEncoding, _sixMonths);

            // Set to a high value so Debug.Assert do not raise exceptions
            reader.GetTestAccessor().NoOfParseError = 100;
            bool res = reader.GetTestAccessor().TryParseRevision(chunk, out _);
            res.Should().BeFalse();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]

        // Avoid launching the difftool at differences
        // APPVEYOR should be detected automatically, this forces the setting (also in local tests)
        // The popup will hang the tests without failure information
        [UseReporter(typeof(AppVeyorReporter))]
        [Test]
        [TestCase("bad_parentid", false)]
        [TestCase("bad_parentid_length", false)]
        [TestCase("bad_sha", false)]
        [TestCase("empty", false)]
        [TestCase("illegal_timestamp", true, false, true)]
        [TestCase("multi_pathfilter", true)]
        [TestCase("no_subject", true)]
        [TestCase("normal", true)]
        [TestCase("short_sha", false)]
        [TestCase("simple_pathfilter", true)]
        [TestCase("subject_no_body", true)]
        [TestCase("empty_commit", true)]
        [TestCase("reflogselector", true, true)]
        public void TryParseRevision_test(string testName, bool expectedReturn, bool hasReflogSelector = false, bool serialThrows = false)
        {
            using (ApprovalResults.ForScenario(testName.Replace(' ', '_')))
            {
                string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData/RevisionReader", testName + ".bin");
                ArraySegment<byte> chunk = File.ReadAllBytes(path);
                RevisionReader reader = RevisionReader.TestAccessor.RevisionReader(new GitModule(""), hasReflogSelector, _logOutputEncoding, _sixMonths);

                // Set to a high value so Debug.Assert do not raise exceptions
                reader.GetTestAccessor().NoOfParseError = 100;
                reader.GetTestAccessor().TryParseRevision(chunk, out GitRevision rev)
                    .Should().Be(expectedReturn);
                if (hasReflogSelector)
                {
                    rev.ReflogSelector.Should().NotBeNull();
                }

                // No LocalTime for the time stamps
                JsonSerializerSettings timeZoneSettings = new()
                {
                    DateTimeZoneHandling = DateTimeZoneHandling.Utc
                };

                if (serialThrows)
                {
                    Action act = () => JsonConvert.SerializeObject(rev);
                    act.Should().Throw<JsonSerializationException>();
                }
                else if (expectedReturn)
                {
                    Approvals.VerifyJson(JsonConvert.SerializeObject(rev, timeZoneSettings));
                }
            }
        }
    }
}
