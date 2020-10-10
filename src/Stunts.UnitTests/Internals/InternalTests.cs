﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace Stunts.UnitTests
{
    public class InternalTests
    {
        private ITestOutputHelper output;

        public InternalTests(ITestOutputHelper output) => this.output = output;

        [Theory]
        [MemberData(nameof(GetPackageVersions))]
        public void CanAccessRequiredInternalsViaReflection(PackageIdentity package, string targetFramework)
        {
            var projectFile = Path.GetFullPath($"Internals\\test-{package}.csproj")!;
            File.WriteAllText(projectFile,
$@"<Project Sdk='Microsoft.NET.Sdk'>
    <PropertyGroup>    
        <OutputType>Exe</OutputType>
        <TargetFramework>{targetFramework}</TargetFramework>
        <EnableDefaultItems>false</EnableDefaultItems>
        <OutputPath>bin\{package.Version}</OutputPath>
        <IntermediateOutputPath>obj\{package.Version}</IntermediateOutputPath>
        <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
        <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
        <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include='{package.Id}' Version='{package.Version}' />
        <PackageReference Include='System.Runtime' Version='4.3.1' />
        <Compile Include='Program.cs' />
        <Compile Include='RoslynInternals.cs' />
    </ItemGroup>
</Project>");

            var projects = new Microsoft.Build.Evaluation.ProjectCollection();
            var result = BuildManager.DefaultBuildManager.Build(
                new BuildParameters(projects)
                {
                    EnableNodeReuse = false,
                    ShutdownInProcNodeOnBuildFinish = true,
                    ResetCaches = true,
                    MaxNodeCount = 1,
                    UseSynchronousLogging = true,
                    LogInitialPropertiesAndItems = true,
                    LogTaskInputs = true,
                    Loggers = new ILogger[]
                    {
                        new Microsoft.Build.Logging.StructuredLogger.StructuredLogger
                        {
                            Verbosity = LoggerVerbosity.Diagnostic,
                            Parameters = Path.ChangeExtension(projectFile, "-restore.binlog")
                        }
                    }
                },
                new BuildRequestData(projectFile, new Dictionary<string, string>
                {
                    { "Configuration", "Debug" }
                }, null, new[] { "Restore" }, null));

            if (result.OverallResult != BuildResultCode.Success)
                Process.Start(Path.ChangeExtension(projectFile, "-restore.binlog"));

            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            result = BuildManager.DefaultBuildManager.Build(
                new BuildParameters(projects)
                {
                    EnableNodeReuse = false,
                    ShutdownInProcNodeOnBuildFinish = true,
                    ResetCaches = true,
                    MaxNodeCount = 1,
                    UseSynchronousLogging = true,
                    LogInitialPropertiesAndItems = true,
                    LogTaskInputs = true,
                    Loggers = new ILogger[]
                    {
                        new Microsoft.Build.Logging.StructuredLogger.StructuredLogger
                        {
                            Verbosity = LoggerVerbosity.Diagnostic,
                            Parameters = Path.ChangeExtension(projectFile, "-build.binlog")
                        }
                    }
                },
                new BuildRequestData(projectFile, new Dictionary<string, string>
                {
                    { "Configuration", "Debug" }
                }, null, new[] { "Build" }, null));

            if (result.OverallResult != BuildResultCode.Success)
                Process.Start(Path.ChangeExtension(projectFile, "-build.binlog"));

            Assert.Equal(BuildResultCode.Success, result.OverallResult);

            var info = new ProcessStartInfo(Path.Combine(
                Path.GetDirectoryName(projectFile) ?? "",
                $@"bin\{package.Version}\{targetFramework}",
                Path.ChangeExtension(Path.GetFileName(projectFile), ".exe")))
            {
                UseShellExecute = false,
                RedirectStandardError = true, 
                CreateNoWindow = true,
            };

            var process = Process.Start(info) ?? throw new InvalidOperationException();
            var error = process.StandardError.ReadToEnd();
            Assert.Equal("", error.Trim());
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
        }

        public static IEnumerable<object[]> GetPackageVersions()
        {
            var providers = Repository.Provider.GetCoreV3();
            var source = new PackageSource("https://api.nuget.org/v3/index.json");
            var repo = new SourceRepository(source, providers);
            var resource = repo.GetResourceAsync<PackageMetadataResource>().Result;
            var metadata = resource.GetMetadataAsync("Microsoft.CodeAnalysis.Workspaces.Common", true, false, new Logger(null), CancellationToken.None).Result;

            // 3.1.0 is already stable and we verified we work with it
            // Older versions are guaranteed to not change either, so we 
            // can rely on it working too, since this test passed at some 
            // point too.
            return metadata
                .Select(m => m.Identity)
                .Where(m => m.Version >= new NuGetVersion("3.1.0"))
                .SelectMany(v => new[]
                {
                    new object[] { v, "net472" },
                    new object[] { v, "net5.0" }
                });
        }

        class Logger : NuGet.Common.ILogger
        {
            readonly ITestOutputHelper? output;

            public Logger(ITestOutputHelper? output) => this.output = output;

            public void LogDebug(string data) => output?.WriteLine($"DEBUG: {data}");
            public void LogVerbose(string data) => output?.WriteLine($"VERBOSE: {data}");
            public void LogInformation(string data) => output?.WriteLine($"INFORMATION: {data}");
            public void LogMinimal(string data) => output?.WriteLine($"MINIMAL: {data}");
            public void LogWarning(string data) => output?.WriteLine($"WARNING: {data}");
            public void LogError(string data) => output?.WriteLine($"ERROR: {data}");
            public void LogErrorSummary(string data) => output?.WriteLine($"ERROR: {data}");
            public void LogSummary(string data) => output?.WriteLine($"SUMMARY: {data}");
            public void LogInformationSummary(string data) => output?.WriteLine($"SUMMARY: {data}");
        }
    }
}