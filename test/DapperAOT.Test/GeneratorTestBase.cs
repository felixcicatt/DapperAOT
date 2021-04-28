﻿using Dapper;
using DapperAOT.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Oracle.ManagedDataAccess.Client;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace DapperAOT.Test
{
    public abstract partial class GeneratorTestBase
    {
        private readonly ITestOutputHelper? _log;
        protected GeneratorTestBase(ITestOutputHelper? log)
            => _log = log;

        [return: NotNullIfNotNull("message")]
        protected string? Log(string? message)
        {
            if (message is not null) _log?.WriteLine(message);
            return message;
        }

        [return: NotNullIfNotNull("message")]
        protected string? Log(SourceText? message)
            => Log(message?.ToString());

        // input from https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md#unit-testing-of-generators

        protected (Compilation? Compilation, GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> Diagnostics) Execute<T>(string source,
            [CallerMemberName] string? name = null) where T : class, ISourceGenerator, new()
        {
            // Create the 'input' compilation that the generator will act on
            if (string.IsNullOrWhiteSpace(name)) name = "compilation";
            Compilation inputCompilation = CreateCompilation(source, name);

            // directly create an instance of the generator
            // (Note: in the compiler this is loaded from an assembly, and created via reflection at runtime)
            T generator = new();
#pragma warning disable CS0618 // Type or member is obsolete
            if (_log is not null && generator is ILoggingAnalyzer logging)
            {
                logging.Log += s => Log(s);
            }
#pragma warning restore CS0618 // Type or member is obsolete

            ShowDiagnostics("Input code", inputCompilation, "CS8795", "CS1701", "CS1702");
            
            // Create the driver that will control the generation, passing in our generator
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

            // Run the generation pass
            // (Note: the generator driver itself is immutable, and all calls return an updated version of the driver that you should use for subsequent calls)
            driver = driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var outputCompilation, out var diagnostics);

            if (_log is not null)
            {
                if (!diagnostics.IsDefaultOrEmpty)
                {
                    Log($"Generator produced {diagnostics.Length} diagnostics:");
                    foreach (var d in diagnostics)
                    {
                        Log(d.ToString());
                    }
                }

                ShowDiagnostics("Output code", outputCompilation, "CS1701", "CS1702");
            }

            // Or we can look at the results directly:
            GeneratorDriverRunResult runResult = driver.GetRunResult();

            return (outputCompilation, runResult, diagnostics);
        }

        void ShowDiagnostics(string caption, Compilation compilation, params string[] ignore)
        {
            if (_log is not null) // useful for finding problems in the input
            {
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var diagnostics = compilation.GetSemanticModel(tree).GetDiagnostics();
                    if (!diagnostics.IsDefaultOrEmpty)
                    {
                        int count = diagnostics.Count(d => !ignore.Contains(d.Id));
                        if (count > 0)
                        {
                            Log($"{caption} has {count} diagnostics from '{tree.FilePath}':");
                            foreach (var d in diagnostics)
                            {
                                if (!ignore.Contains(d.Id)) // partial without implementation; we know - that's what we're here to fix
                                {
                                    Log(d.ToString());
                                }
                            }
                        }
                    }
                }
            }
        }

        protected static Compilation CreateCompilation(string source, string name)
           => CSharpCompilation.Create(name,
               syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source).WithFilePath("input.cs") },
               references: new[] {
                   MetadataReference.CreateFromFile(typeof(Binder).Assembly.Location),
                   MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                   MetadataReference.CreateFromFile(Assembly.Load("System.Data").Location),
                   MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
                   MetadataReference.CreateFromFile(typeof(DbConnection).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(OracleConnection).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(Component).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(CommandAttribute).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(SqlMapper).Assembly.Location),
               },
               options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));
    }
}
