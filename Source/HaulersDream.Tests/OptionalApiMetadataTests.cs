using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    // Startup enumerates private methods and attributes before optional integrations are active.
    // A guarded method BODY may reference MP; its signature/base/fields must not require MP.
    // MetadataLoadContext reads Krafs's non-executable references without CLR type validation.
    [TestFixture]
    public class OptionalApiMetadataTests
    {
        private string directory;
        private string[] inputs;

        [OneTimeSetUp]
        public void BuildControls()
        {
            var manifest = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "OptionalApiMetadataInputs.txt"));
            // Publicizer can return a path relative to the game project's directory.
            inputs = manifest.Skip(1).Select(p => Path.GetFullPath(Path.IsPathRooted(p)
                ? p : Path.Combine(manifest[0], p))).ToArray();
            directory = Path.Combine(Path.GetTempPath(), "HaulersDream.MetadataTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string api = Path.Combine(directory, "0MultiplayerAPI.dll");
            Compile(api, "namespace Multiplayer.API { public class SyncWorker {} } ");
            // A dependency loaded by the parent must not make the isolated probe pass by accident.
            Assembly.Load(File.ReadAllBytes(api));
            Assert.That(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0MultiplayerAPI"), Is.True);

            const string declarations = @"
                public sealed class ExplosiveAttribute : System.Attribute {
                    public ExplosiveAttribute() { throw new System.Exception(""Attribute executed""); }
                }
                [Explosive] public static class Fixture {
                    static Fixture() { throw new System.Exception(""Initializer executed""); }
                    BODY
                }";
            Compile(Path.Combine(directory, "BodyOnly.dll"), declarations.Replace("BODY",
                "private static void Inspect(object worker) { var typed = (Multiplayer.API.SyncWorker)worker; }"), api);
            Compile(Path.Combine(directory, "TypedParameter.dll"), declarations.Replace("BODY",
                "private static void Inspect(Multiplayer.API.SyncWorker worker) {}"), api);
            Compile(Path.Combine(directory, "CachedField.dll"), declarations.Replace("BODY",
                "private static System.Collections.Generic.List<Multiplayer.API.SyncWorker> cached;"), api);
        }

        [OneTimeTearDown]
        public void RemoveControls()
        {
            // Only this test's freshly created, fully qualified temporary directory is owned.
            if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void CompiledModMetadataDoesNotRequireMultiplayerApi()
        {
            string product = inputs.Single(p => Path.GetFileName(p) == "HaulersDream.dll");
            Assert.That(File.Exists(product), Is.True, "The current mod must be built before inspection.");
            TestContext.WriteLine(Inspect(product));
        }

        [Test]
        public void GuardedBodyReferenceAndAttributeMetadataDoNotExecuteCode()
        {
            TestContext.WriteLine(Inspect(Path.Combine(directory, "BodyOnly.dll")));
        }

        [TestCase("TypedParameter")]
        [TestCase("CachedField")]
        public void OptionalMetadataDependencyIsRejectedEvenWhenParentLoadedIt(string control)
        {
            var failure = Assert.Throws<InvalidOperationException>(() =>
                Inspect(Path.Combine(directory, control + ".dll")));
            Assert.That(failure.Message, Does.Contain("0MultiplayerAPI"));
        }

        private string Inspect(string product)
        {
            var domain = AppDomain.CreateDomain("Absent optional API " + Guid.NewGuid(), null,
                new AppDomainSetup {
                    ApplicationBase = TestContext.CurrentContext.TestDirectory,
                    ConfigurationFile = typeof(OptionalApiMetadataTests).Assembly.Location + ".config"
                });
            try
            {
                var probe = (OptionalApiMetadataProbe)domain.CreateInstanceAndUnwrap(
                    typeof(OptionalApiMetadataProbe).Assembly.FullName, typeof(OptionalApiMetadataProbe).FullName);
                return probe.Inspect(product, inputs);
            }
            finally { AppDomain.Unload(domain); }
        }

        private static void Compile(string output, string source, params string[] references)
        {
            using var compiler = new CSharpCodeProvider();
            var options = new CompilerParameters(new[] { "System.dll", "System.Core.dll" }.Concat(references).ToArray(), output)
                { GenerateExecutable = false, GenerateInMemory = false };
            var result = compiler.CompileAssemblyFromSource(options, source);
            Assert.That(result.Errors.HasErrors, Is.False,
                string.Join(Environment.NewLine, result.Errors.Cast<CompilerError>()));
        }
    }

    // MarshalByRef keeps every inspected assembly in a fresh AppDomain; the parent test host may
    // already have an optional integration loaded. No game or attribute constructor is executed.
    public sealed class OptionalApiMetadataProbe : MarshalByRefObject
    {
        private const string OptionalApi = "0MultiplayerAPI";
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        public string Inspect(string product, string[] referencePaths)
        {
            string context = product;
            int types = 0, methods = 0, fields = 0;
            var resolver = new WithoutOptionalApi(referencePaths);
            using var metadata = new MetadataLoadContext(resolver, "mscorlib");
            try
            {
                RequireAbsent(metadata);
                foreach (Type type in metadata.LoadFromAssemblyPath(product).GetTypes())
                {
                    context = type.FullName; types++;
                    CheckType(type.BaseType);
                    foreach (Type implemented in type.GetInterfaces()) CheckType(implemented);
                    foreach (Type argument in type.GetGenericArguments()) CheckType(argument);
                    CheckAttributes(type.GetCustomAttributesData());
                    foreach (FieldInfo field in type.GetFields(Declared))
                    {
                        context = type.FullName + "." + field.Name; fields++;
                        CheckType(field.FieldType);
                    }
                    foreach (MethodBase method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
                    {
                        context = type.FullName + "." + method.Name; methods++;
                        if (method is MethodInfo info) CheckType(info.ReturnType);
                        foreach (ParameterInfo parameter in method.GetParameters()) CheckType(parameter.ParameterType);
                        if (method.IsGenericMethod)
                            foreach (Type argument in method.GetGenericArguments()) CheckType(argument);
                        CheckAttributes(method.GetCustomAttributesData());
                    }
                }
                RequireAbsent(metadata);
                if (resolver.Requests != 0) throw new InvalidOperationException("Metadata requested " + OptionalApi);
                return $"Inspected {types} types, {methods} methods/constructors and {fields} fields; optional API requests=0.";
            }
            // Do not marshal the original exception: it can contain inspected Type/MethodInfo
            // objects and make the parent try to load the very assembly this probe isolates.
            catch (Exception error)
            {
                string loaders = error is ReflectionTypeLoadException failed
                    ? string.Join(Environment.NewLine, failed.LoaderExceptions.Select(e => e.ToString()).Distinct()) : "";
                throw new InvalidOperationException("Metadata inspection failed at " + context + ": " + error + Environment.NewLine + loaders);
            }
        }

        private sealed class WithoutOptionalApi : MetadataAssemblyResolver
        {
            private readonly Dictionary<string, string> references;
            internal int Requests;
            internal WithoutOptionalApi(string[] paths) => references = paths.Distinct().ToDictionary(
                Path.GetFileNameWithoutExtension, p => p, StringComparer.OrdinalIgnoreCase);
            public override Assembly Resolve(MetadataLoadContext context, AssemblyName name)
            {
                if (name.Name == OptionalApi)
                {
                    Requests++;
                    throw new FileNotFoundException("Optional dependency deliberately unavailable: " + OptionalApi);
                }
                return references.TryGetValue(name.Name, out string path) ? context.LoadFromAssemblyPath(path) : null;
            }
        }

        private static void RequireAbsent(MetadataLoadContext metadata)
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Concat(AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies())
                .Concat(metadata.GetAssemblies())
                .Any(a => a.GetName().Name == OptionalApi))
                throw new InvalidOperationException("Probe already loaded " + OptionalApi);
        }

        private static void CheckAttributes(IEnumerable<CustomAttributeData> attributes)
        {
            foreach (var attribute in attributes) CheckType(attribute.AttributeType);
        }

        private static void CheckType(Type type) => CheckType(type, new HashSet<Type>());

        private static void CheckType(Type type, HashSet<Type> visited)
        {
            if (type == null || !visited.Add(type)) return;
            if (type.Assembly.GetName().Name == OptionalApi) throw new InvalidOperationException("Optional metadata type " + type);
            if (type.HasElementType) CheckType(type.GetElementType(), visited);
            if (type.IsGenericParameter)
            {
                foreach (Type constraint in type.GetGenericParameterConstraints()) CheckType(constraint, visited);
            }
            else if (type.IsGenericType)
                foreach (Type argument in type.GetGenericArguments()) CheckType(argument, visited);
        }
    }
}
