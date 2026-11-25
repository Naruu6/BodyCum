using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Small local compiler adapter for the existing Build-BodyCum.ps1 command line.
// Host dependencies run on .NET Framework; output references remain explicitly supplied game DLLs.
internal static class RoslynCscHost
{
    private static readonly string RoslynDirectory = Environment.GetEnvironmentVariable("BODYCUM_ROSLYN_PATH") ?? InstalledCompilerPath.RoslynDirectory;
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveCompilerAssembly;
        try { return Compile(args); }
        catch (Exception error) { Console.Error.WriteLine("Local Roslyn compiler: " + error); return 1; }
    }
    private static Assembly ResolveCompilerAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name + ".dll";
        var path = Path.Combine(RoslynDirectory, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Compile(string[] args)
    {
        if (args.Length == 1 && (args[0] == "/version" || args[0] == "--version"))
        {
            Console.WriteLine("Installed Roslyn " + typeof(CSharpCompilation).Assembly.GetName().Version + " / C# 7.3 local adapter");
            return 0;
        }
        var references = new List<MetadataReference>();
        var sources = new List<string>();
        var resources = new List<ResourceDescription>();
        var outputKind = OutputKind.DynamicallyLinkedLibrary;
        var optimize = false;
        string output = null;
        foreach (var arg in args)
        {
            if (arg.Equals("/noconfig", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/nologo", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/nostdlib+", StringComparison.OrdinalIgnoreCase)) continue;
            if (arg.Equals("/optimize+", StringComparison.OrdinalIgnoreCase)) { optimize = true; continue; }
            if (arg.Equals("/optimize-", StringComparison.OrdinalIgnoreCase)) { optimize = false; continue; }
            if (arg.Equals("/target:library", StringComparison.OrdinalIgnoreCase)) { outputKind = OutputKind.DynamicallyLinkedLibrary; continue; }
            if (arg.Equals("/target:exe", StringComparison.OrdinalIgnoreCase)) { outputKind = OutputKind.ConsoleApplication; continue; }
            if (arg.StartsWith("/out:", StringComparison.OrdinalIgnoreCase)) { output = Path.GetFullPath(arg.Substring(5)); continue; }
            if (arg.StartsWith("/reference:", StringComparison.OrdinalIgnoreCase))
            { references.Add(MetadataReference.CreateFromFile(Path.GetFullPath(arg.Substring(11)))); continue; }
            if (arg.StartsWith("/resource:", StringComparison.OrdinalIgnoreCase))
            {
                var spec = arg.Substring(10);
                var comma = spec.LastIndexOf(',');
                if (comma <= 0 || comma == spec.Length - 1) throw new ArgumentException("Expected /resource:path,logicalName");
                var path = Path.GetFullPath(spec.Substring(0, comma));
                var logicalName = spec.Substring(comma + 1);
                resources.Add(new ResourceDescription(logicalName, () => File.OpenRead(path), true));
                continue;
            }
            if (arg.StartsWith("/", StringComparison.Ordinal) || arg.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Unsupported compiler option: " + arg);
            sources.Add(Path.GetFullPath(arg));
        }
        if (output == null || sources.Count == 0 || references.Count == 0)
            throw new ArgumentException("Expected /out:, explicit /reference: inputs and C# sources.");
        var parse = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var trees = new List<SyntaxTree>();
        foreach (var file in sources)
            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), parse, file, Encoding.UTF8));
        var options = new CSharpCompilationOptions(outputKind)
            .WithOptimizationLevel(optimize ? OptimizationLevel.Release : OptimizationLevel.Debug);
        var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(output), trees, references, options);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        using (var stream = File.Create(output))
        {
            var result = compilation.Emit(stream, manifestResources: resources);
            foreach (var diagnostic in result.Diagnostics)
                if (diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Severity == DiagnosticSeverity.Warning)
                    Console.Error.WriteLine(diagnostic.ToString());
            return result.Success ? 0 : 1;
        }
    }
}
