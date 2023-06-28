﻿using Basic.CompilerLog.Util;
using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Xunit;

namespace Basic.CompilerLog.UnitTests;

public sealed class CompilerLogFixture : IDisposable
{
    internal string StorageDirectory { get; }
    
    /// <summary>
    /// Directory that holds the log files
    /// </summary>
    internal string ComplogDirectory { get; }

    internal string ConsoleComplogPath { get; }

    internal string ClassLibComplogPath { get; }

    internal string ClassLibSignedComplogPath { get; }

    /// <summary>
    /// A multi-targeted class library
    /// </summary>
    internal string ClassLibMultiComplogPath { get; }

    internal string? WpfAppComplogPath { get; }

    internal IEnumerable<string> AllComplogs { get; }

    public CompilerLogFixture()
    {
        StorageDirectory = Path.Combine(Path.GetTempPath(), nameof(CompilerLogFixture), Guid.NewGuid().ToString("N"));
        ComplogDirectory = Path.Combine(StorageDirectory, "logs");
        Directory.CreateDirectory(ComplogDirectory);

        var allCompLogs = new List<string>();
        ConsoleComplogPath = WithBuild("console.complog", static string (string scratchPath) =>
        {
            DotnetUtil.CommandOrThrow($"new console --name example --output .", scratchPath);
            var projectFileContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net7.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(scratchPath, "example.csproj"), projectFileContent, TestBase.DefaultEncoding);
            var program = """
                using System;
                using System.Text.RegularExpressions;
                // This is an amazing resource
                var r = Util.GetRegex();
                Console.WriteLine(r);

                partial class Util {
                    [GeneratedRegex("abc|def", RegexOptions.IgnoreCase, "en-US")]
                    internal static partial Regex GetRegex();
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Program.cs"), program, TestBase.DefaultEncoding);
            Assert.True(DotnetUtil.Command("build -bl", scratchPath).Succeeded);
            return Path.Combine(scratchPath, "msbuild.binlog");
        });
        
        ClassLibComplogPath = WithBuild("classlib.complog", static string (string scratchPath) =>
        {
            DotnetUtil.CommandOrThrow($"new classlib --name example --output .", scratchPath);
            var projectFileContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net7.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(scratchPath, "example.csproj"), projectFileContent, TestBase.DefaultEncoding);
            var program = """
                using System;
                using System.Text.RegularExpressions;

                partial class Util {
                    [GeneratedRegex("abc|def", RegexOptions.IgnoreCase, "en-US")]
                    internal static partial Regex GetRegex();
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Class1.cs"), program, TestBase.DefaultEncoding);
            Assert.True(DotnetUtil.Command("build -bl", scratchPath).Succeeded);
            return Path.Combine(scratchPath, "msbuild.binlog");
        });

        ClassLibSignedComplogPath = WithBuild("classlibsigned.complog", static string (string scratchPath) =>
        {
            DotnetUtil.CommandOrThrow($"new classlib --name example --output .", scratchPath);
            var keyFilePath = Path.Combine(scratchPath, "Key.snk");
            var projectFileContent = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net7.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <KeyOriginatorFile>{keyFilePath}</KeyOriginatorFile>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(scratchPath, "example.csproj"), projectFileContent, TestBase.DefaultEncoding);
            File.WriteAllBytes(keyFilePath, ResourceLoader.GetResourceBlob("Key.snk"));
            var program = """
                using System;
                using System.Text.RegularExpressions;

                partial class Util {
                    [GeneratedRegex("abc|def", RegexOptions.IgnoreCase, "en-US")]
                    internal static partial Regex GetRegex();
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Class1.cs"), program, TestBase.DefaultEncoding);
            Assert.True(DotnetUtil.Command("build -bl", scratchPath).Succeeded);
            return Path.Combine(scratchPath, "msbuild.binlog");
        });

        ClassLibMultiComplogPath = WithBuild("classlibmulti.complog", static string (string scratchPath) =>
        {
            DotnetUtil.CommandOrThrow($"new classlib --name example --output .", scratchPath);
            var projectFileContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net6.0;net7.0</TargetFrameworks>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(Path.Combine(scratchPath, "example.csproj"), projectFileContent, TestBase.DefaultEncoding);
            var program = """
                using System;
                using System.Text.RegularExpressions;

                partial class Util {
                    internal static Regex GetRegex() => null!;
                }
                """;
            File.WriteAllText(Path.Combine(scratchPath, "Class 1.cs"), program, TestBase.DefaultEncoding);
            Assert.True(DotnetUtil.Command("build -bl", scratchPath).Succeeded);
            return Path.Combine(scratchPath, "msbuild.binlog");
        });

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WpfAppComplogPath = WithBuild("wpfapp.complog", static string (string scratchPath) =>
            {
                Assert.True(DotnetUtil.Command("new wpf --name example --output .", scratchPath).Succeeded);
                Assert.True(DotnetUtil.Command("build -bl", scratchPath).Succeeded);
                return Path.Combine(scratchPath, "msbuild.binlog");
            });
        }

        AllComplogs = allCompLogs;
        string WithBuild(string name, Func<string, string> action)
        {
            var scratchPath = Path.Combine(StorageDirectory, "scratch dir");
            Directory.CreateDirectory(scratchPath);
            var binlogFilePath = action(scratchPath);
            var complogFilePath = Path.Combine(ComplogDirectory, name);
            var diagnostics = CompilerLogUtil.ConvertBinaryLog(binlogFilePath, complogFilePath);
            Assert.Empty(diagnostics);
            Directory.Delete(scratchPath, recursive: true);
            allCompLogs.Add(complogFilePath);
            return complogFilePath;
        }
    }

    public void Dispose()
    {
        Directory.Delete(StorageDirectory, recursive: true);
    }
}

[CollectionDefinition(Name)]
public sealed class CompilerLogCollection : ICollectionFixture<CompilerLogFixture>
{
    public const string Name = "Compiler Log Collection";
}
