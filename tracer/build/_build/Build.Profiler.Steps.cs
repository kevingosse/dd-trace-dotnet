using Nuke.Common;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.MSBuild;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.MSBuild.MSBuildTasks;
using Nuke.Common.Tools.NuGet;

partial class Build
{
    Target CompileProfilerManagedSrc => _ => _
        .Unlisted()
        .Description("Compiles the managed code in the src directory")
        .After(CreateRequiredDirectories)
        .After(Restore)
        .Executes(() =>
        {
            List<string> projects = new();
            projects.Add(SharedDirectory.GlobFiles("**/Datadog.AutoInstrumentation.ManagedLoader.csproj").Single());
            projects.Add(ProfilerDirectory.GlobFiles("**/Datadog.Profiler.Managed.csproj").Single());

            // Build the shared managed loader
            DotNetBuild(s => s
                .SetTargetPlatformAnyCPU()
                .SetConfiguration(BuildConfiguration)
                .CombineWith(projects, (x, project) => x
                    .SetProjectFile(project)));
        });

    Target CompileProfilerNativeSrc => _ => _
        .Unlisted()
        .Description("Compiles the native profiler assets")
        .DependsOn(CompileProfilerNativeSrcWindows)
        .DependsOn(CompileProfilerNativeSrcAndTestLinux);

    Target CompileProfilerNativeSrcWindows => _ => _
        .Unlisted()
        .After(CompileProfilerManagedSrc) // Keeping this because this may depend on embedding managed libs
        .OnlyWhenStatic(() => IsWin)
        .Executes(() =>
        {
            var project = ProfilerDirectory.GlobFiles("**/Datadog.Profiler.Native.Windows.vcxproj").Single();

            var nugetPackageRestoreDirectory = RootDirectory / "packages";

            NuGetTasks.NuGetRestore(s => s
                .SetTargetPath(project)
                .SetVerbosity(NuGetVerbosity.Normal)
                .SetPackagesDirectory(nugetPackageRestoreDirectory));

            // If we're building for x64, build for x86 too
            var platforms =
                Equals(TargetPlatform, MSBuildTargetPlatform.x64)
                    ? new[] { MSBuildTargetPlatform.x64, MSBuildTargetPlatform.x86 }
                    : new[] { MSBuildTargetPlatform.x86 };

            // Can't use dotnet msbuild, as needs to use the VS version of MSBuild
            // Build native profiler assets
            MSBuild(s => s
                .SetTargetPath(project)
                .SetConfiguration(BuildConfiguration)
                .SetProperty("SpectreMitigation", "false") // Enforce the same build in all CI environments
                .SetMSBuildPath()
                .DisableRestore()
                .SetMaxCpuCount(null)
                .CombineWith(platforms, (m, platform) => m
                    .SetTargetPlatform(platform)));
        });

    Target CompileProfilerNativeSrcAndTestLinux => _ => _
        .Unlisted()
        .Description("Compile Profiler native code")
        .After(CompileProfilerManagedSrc)
        .OnlyWhenStatic(() => IsLinux)
        .Executes(() =>
        {
            EnsureExistingDirectory(ProfilerLinuxBuildDirectory);

            CMake.Value(
                arguments: $"-S {ProfilerDirectory}",
                workingDirectory: ProfilerLinuxBuildDirectory);

            Make.Value(workingDirectory: ProfilerLinuxBuildDirectory);

            if (IsAlpine)
            {
                // On Alpine, we do not have permission to access the file libunwind-prefix/src/libunwind/config/config.guess
                // Make the whole folder and its content accessible by everyone to make sure the upload process does not fail
                Chmod.Value.Invoke(" -R 777 " + ProfilerLinuxBuildDirectory);
            }
        });

    Target RunProfilerNativeUnitTestsLinux => _ => _
        .Unlisted()
        .Description("Run profiler native unit tests")
        .OnlyWhenStatic(() => IsLinux)
        .After(CompileProfilerNativeSrcAndTestLinux)
        .Executes(() =>
        {
            var workingDirectory = ProfilerOutputDirectory / "bin" / "Datadog.Profiler.Native.Tests";
            EnsureExistingDirectory(workingDirectory);

            var exePath = workingDirectory / "Datadog.Profiler.Native.Tests";
            Chmod.Value.Invoke("+x " + exePath);

            var testExe = ToolResolver.GetLocalTool(exePath);
            testExe("--gtest_output=xml", workingDirectory: workingDirectory);
        });

    Target CompileProfilerNativeTestsWindows => _ => _
        .Unlisted()
        .After(CompileProfilerNativeSrc)
        .OnlyWhenStatic(() => IsWin)
        .Executes(() =>
        {
            // If we're building for x64, build for x86 too
            var platforms =
                Equals(TargetPlatform, MSBuildTargetPlatform.x64)
                    ? new[] { MSBuildTargetPlatform.x64, MSBuildTargetPlatform.x86 }
                    : new[] { MSBuildTargetPlatform.x86 };

            // Can't use dotnet msbuild, as needs to use the VS version of MSBuild
            MSBuild(s => s
                .SetTargetPath(ProfilerMsBuildProject)
                .SetConfiguration(BuildConfiguration)
                .SetMSBuildPath()
                .SetTargets("BuildCppTests")
                .DisableRestore()
                .SetMaxCpuCount(null)
                .CombineWith(platforms, (m, platform) => m
                    .SetTargetPlatform(platform)));
        });

    Target RunProfilerNativeUnitTestsWindows => _ => _
        .Unlisted()
        .After(CompileProfilerNativeTestsWindows)
        .OnlyWhenStatic(() => IsWin)
        .Executes(() =>
        {
            var configAndTarget = $"{BuildConfiguration}-{TargetPlatform}";
            var workingDirectory = ProfilerOutputDirectory / "bin" / configAndTarget / "profiler" / "test" / "Datadog.Profiler.Native.Tests";
            EnsureExistingDirectory(workingDirectory);

            var exePath = workingDirectory / "Datadog.Profiler.Native.Tests.exe";
            var testExe = ToolResolver.GetLocalTool(exePath);
            testExe("--gtest_output=xml", workingDirectory: workingDirectory);
        });

}
