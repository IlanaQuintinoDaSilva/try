﻿using System;
using System.Runtime.CompilerServices;
using WorkspaceServer.Models.Execution;
using WorkspaceServer.Servers.OmniSharp;
using Xunit.Abstractions;

namespace WorkspaceServer.Tests
{
    public class DotnetWorkspaceServerTests : WorkspaceServerTests
    {
        public DotnetWorkspaceServerTests(ITestOutputHelper output) : base(output)
        {
        }

        protected override RunRequest CreateRunRequestContaining(string text)
        {
            return new RunRequest(
                $@"using System; using System.Linq; using System.Collections.Generic; class Program {{ static void Main() {{ {text}
                    }}
                }}
");
        }

        protected override IWorkspaceServer GetWorkspaceServer(
            int defaultTimeoutInSeconds = 10,
            [CallerMemberName] string testName = null)
        {
            var project = Create.TestWorkspace(testName);

            var workspaceServer = new DotnetWorkspaceServer(project, defaultTimeoutInSeconds);

            RegisterForDisposal(workspaceServer);

            return workspaceServer;
        }
    }
}
