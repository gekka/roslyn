﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Symbols.Retargeting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class CollectionExpressionTests : CSharpTestBase
    {
        private static string IncludeExpectedOutput(string expectedOutput) => ExecutionConditionUtil.IsMonoOrCoreClr ? expectedOutput : null;

        private const string s_collectionExtensions = """
            using System;
            using System.Collections;
            using System.Linq;
            using System.Text;
            static partial class CollectionExtensions
            {
                private static void Append(StringBuilder builder, bool isFirst, object value)
                {
                    if (!isFirst) builder.Append(", ");
                    if (value is IEnumerable e && value is not string)
                    {
                        AppendCollection(builder, e);
                    }
                    else
                    {
                        builder.Append(value is null ? "null" : value.ToString());
                    }
                }
                private static void AppendCollection(StringBuilder builder, IEnumerable e)
                {
                    builder.Append("[");
                    bool isFirst = true;
                    foreach (var i in e)
                    {
                        Append(builder, isFirst, i);
                        isFirst = false;
                    }
                    builder.Append("]");
                }
                internal static void Report(this object o, bool includeType = false)
                {
                    var builder = new StringBuilder();
                    Append(builder, isFirst: true, o);
                    if (includeType) Console.Write("({0}) ", GetTypeName(o.GetType()));
                    Console.Write(builder.ToString());
                    Console.Write(", ");
                }
                internal static string GetTypeName(this Type type)
                {
                    if (type.IsArray)
                    {
                        return GetTypeName(type.GetElementType()) + "[]";
                    }
                    string typeName = type.Name;
                    int index = typeName.LastIndexOf('`');
                    if (index >= 0)
                    {
                        typeName = typeName.Substring(0, index);
                    }
                    if (!type.IsGenericParameter)
                    {
                        if (type.DeclaringType is { } declaringType)
                        {
                            typeName = Concat(GetTypeName(declaringType), typeName);
                        }
                        else
                        {
                            typeName = Concat(type.Namespace, typeName);
                        }
                    }
                    if (!type.IsGenericType)
                    {
                        return typeName;
                    }
                    var typeArgs = type.GetGenericArguments();
                    return $"{typeName}<{string.Join(", ", typeArgs.Select(GetTypeName))}>";
                }
                private static string Concat(string container, string name)
                {
                    return string.IsNullOrEmpty(container) ? name : container + "." + name;
                }
            }
            """;
        private const string s_collectionExtensionsWithSpan = s_collectionExtensions +
            """
            static partial class CollectionExtensions
            {
                internal static void Report<T>(this in Span<T> s)
                {
                    Report((ReadOnlySpan<T>)s);
                }
                internal static void Report<T>(this in ReadOnlySpan<T> s)
                {
                    var builder = new StringBuilder();
                    builder.Append("[");
                    bool isFirst = true;
                    foreach (var i in s)
                    {
                        Append(builder, isFirst, i);
                        isFirst = false;
                    }
                    builder.Append("]");
                    Console.Write(builder.ToString());
                    Console.Write(", ");
                }
            }
            """;

        [Theory]
        [InlineData(LanguageVersion.CSharp11)]
        [InlineData(LanguageVersion.Preview)]
        public void LanguageVersionDiagnostics(LanguageVersion languageVersion)
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        object[] x = [];
                        List<object> y = [1, 2, 3];
                        List<object[]> z = [[]];
                    }
                }
                """;
            var comp = CreateCompilation(source, parseOptions: TestOptions.Regular.WithLanguageVersion(languageVersion));
            if (languageVersion == LanguageVersion.CSharp11)
            {
                comp.VerifyEmitDiagnostics(
                    // (6,22): error CS9058: Feature 'collection expressions' is not available in C# 11.0. Please use language version 12.0 or greater.
                    //         object[] x = [];
                    Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion11, "[").WithArguments("collection expressions", "12.0").WithLocation(6, 22),
                    // (7,26): error CS9058: Feature 'collection expressions' is not available in C# 11.0. Please use language version 12.0 or greater.
                    //         List<object> y = [1, 2, 3];
                    Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion11, "[").WithArguments("collection expressions", "12.0").WithLocation(7, 26),
                    // (8,28): error CS9058: Feature 'collection expressions' is not available in C# 11.0. Please use language version 12.0 or greater.
                    //         List<object[]> z = [[]];
                    Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion11, "[").WithArguments("collection expressions", "12.0").WithLocation(8, 28),
                    // (8,29): error CS9058: Feature 'collection expressions' is not available in C# 11.0. Please use language version 12.0 or greater.
                    //         List<object[]> z = [[]];
                    Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion11, "[").WithArguments("collection expressions", "12.0").WithLocation(8, 29));
            }
            else
            {
                comp.VerifyEmitDiagnostics();
            }
        }

        [Fact]
        public void NaturalType_01()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object x = [];
                        dynamic y = [];
                        var z = [];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,20): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object x = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("object").WithLocation(5, 20),
                // (6,21): error CS9174: Cannot initialize type 'dynamic' with a collection expression because the type is not constructible.
                //         dynamic y = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("dynamic").WithLocation(6, 21),
                // (7,17): error CS9176: There is no target type for the collection expression.
                //         var z = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 17));

            var tree = comp.SyntaxTrees[0];
            var model = comp.GetSemanticModel(tree);
            var collections = tree.GetRoot().DescendantNodes().OfType<CollectionExpressionSyntax>().ToArray();
            Assert.Equal(3, collections.Length);
            VerifyTypes(model, collections[0], expectedType: null, expectedConvertedType: "System.Object", ConversionKind.NoConversion);
            VerifyTypes(model, collections[1], expectedType: null, expectedConvertedType: "dynamic", ConversionKind.NoConversion);
            VerifyTypes(model, collections[2], expectedType: null, expectedConvertedType: "?", ConversionKind.NoConversion);
        }

        [Fact]
        public void NaturalType_02()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object x = [1];
                        dynamic y = [2];
                        var z = [3];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,20): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object x = [1];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1]").WithArguments("object").WithLocation(5, 20),
                // (6,21): error CS9174: Cannot initialize type 'dynamic' with a collection expression because the type is not constructible.
                //         dynamic y = [2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("dynamic").WithLocation(6, 21),
                // (7,17): error CS9176: There is no target type for the collection expression.
                //         var z = [3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[3]").WithLocation(7, 17));

            var tree = comp.SyntaxTrees[0];
            var model = comp.GetSemanticModel(tree);
            var collections = tree.GetRoot().DescendantNodes().OfType<CollectionExpressionSyntax>().ToArray();
            Assert.Equal(3, collections.Length);
            VerifyTypes(model, collections[0], expectedType: null, expectedConvertedType: "System.Object", ConversionKind.NoConversion);
            VerifyTypes(model, collections[1], expectedType: null, expectedConvertedType: "dynamic", ConversionKind.NoConversion);
            VerifyTypes(model, collections[2], expectedType: null, expectedConvertedType: "?", ConversionKind.NoConversion);
        }

        [Fact]
        public void NaturalType_03()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object x = [1, ""];
                        dynamic y = [2, ""];
                        var z = [3, ""];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,20): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object x = [1, ""];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, @"[1, """"]").WithArguments("object").WithLocation(5, 20),
                // (6,21): error CS9174: Cannot initialize type 'dynamic' with a collection expression because the type is not constructible.
                //         dynamic y = [2, ""];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, @"[2, """"]").WithArguments("dynamic").WithLocation(6, 21),
                // (7,17): error CS9176: There is no target type for the collection expression.
                //         var z = [3, ""];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, @"[3, """"]").WithLocation(7, 17));
        }

        [Fact]
        public void NaturalType_04()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object x = [null];
                        dynamic y = [null];
                        var z = [null];
                        int?[] w = [null];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,20): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object x = [null];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[null]").WithArguments("object").WithLocation(5, 20),
                // (6,21): error CS9174: Cannot initialize type 'dynamic' with a collection expression because the type is not constructible.
                //         dynamic y = [null];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[null]").WithArguments("dynamic").WithLocation(6, 21),
                // (7,17): error CS9176: There is no target type for the collection expression.
                //         var z = [null];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[null]").WithLocation(7, 17));
        }

        [Fact]
        public void NaturalType_05()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        var x = [1, 2, null];
                        object y = [1, 2, null];
                        dynamic z = [1, 2, null];
                        int?[] w = [1, 2, null];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,17): error CS9176: There is no target type for the collection expression.
                //         var x = [1, 2, null];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[1, 2, null]").WithLocation(5, 17),
                // (6,20): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object y = [1, 2, null];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2, null]").WithArguments("object").WithLocation(6, 20),
                // (7,21): error CS9174: Cannot initialize type 'dynamic' with a collection expression because the type is not constructible.
                //         dynamic z = [1, 2, null];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2, null]").WithArguments("dynamic").WithLocation(7, 21));
        }

        [Fact]
        public void NaturalType_06()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object[] x = [[]];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,23): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object[] x = [[]];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("object").WithLocation(5, 23));
        }

        [Fact]
        public void NaturalType_07()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object[] y = [[2]];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,23): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object[] y = [[2]];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("object").WithLocation(5, 23));
        }

        [Fact]
        public void NaturalType_08()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        var z = [[3]];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,17): error CS9176: There is no target type for the collection expression.
                //         var z = [[3]];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[[3]]").WithLocation(5, 17));
        }

        [Fact]
        public void NaturalType_09()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        F([]);
                        F([[]]);
                    }
                    static T F<T>(T t) => t;
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,9): error CS0411: The type arguments for method 'Program.F<T>(T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T)").WithLocation(5, 9),
                // (6,9): error CS0411: The type arguments for method 'Program.F<T>(T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([[]]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T)").WithLocation(6, 9));
        }

        [Fact]
        public void NaturalType_10()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        F([1, 2]).Report();
                        F([[3, 4]]).Report();
                    }
                    static T F<T>(T t) => t;
                }
                """;
            var comp = CreateCompilation(new[] { source, s_collectionExtensions });
            comp.VerifyEmitDiagnostics(
                // 0.cs(5,9): error CS0411: The type arguments for method 'Program.F<T>(T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([1, 2]).Report();
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T)").WithLocation(5, 9),
                // 0.cs(6,9): error CS0411: The type arguments for method 'Program.F<T>(T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([[3, 4]]).Report();
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T)").WithLocation(6, 9));
        }

        [Fact]
        public void NaturalType_11()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        var d1 = () => [];
                        Func<int[]> d2 = () => [];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,18): error CS8917: The delegate type could not be inferred.
                //         var d1 = () => [];
                Diagnostic(ErrorCode.ERR_CannotInferDelegateType, "() => []").WithLocation(6, 18));
        }

        [Fact]
        public void InterfaceType_01()
        {
            string source = """
                using System.Collections;
                class Program
                {
                    static void Main()
                    {
                        IEnumerable a = [1];
                        ICollection b = [2];
                        IList c = [3];
                        a.Report(includeType: true);
                        b.Report(includeType: true);
                        c.Report(includeType: true);
                    }
                }
                """;
            var comp = CreateCompilation(new[] { source, s_collectionExtensions });
            comp.VerifyEmitDiagnostics(
                // 0.cs(6,25): error CS9174: Cannot initialize type 'IEnumerable' with a collection expression because the type is not constructible.
                //         IEnumerable a = [1];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1]").WithArguments("System.Collections.IEnumerable").WithLocation(6, 25),
                // 0.cs(7,25): error CS9174: Cannot initialize type 'ICollection' with a collection expression because the type is not constructible.
                //         ICollection b = [2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("System.Collections.ICollection").WithLocation(7, 25),
                // 0.cs(8,19): error CS9174: Cannot initialize type 'IList' with a collection expression because the type is not constructible.
                //         IList c = [3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[3]").WithArguments("System.Collections.IList").WithLocation(8, 19));
        }

        [Fact]
        public void InterfaceType_02()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        IEnumerable<int> a = [];
                        ICollection<int> b = [];
                        IList<int> c = [];
                        IReadOnlyCollection<int> d = [];
                        IReadOnlyList<int> e = [];
                        a.Report(includeType: true);
                        b.Report(includeType: true);
                        c.Report(includeType: true);
                        d.Report(includeType: true);
                        e.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensions },
                expectedOutput: "(System.Int32[]) [], (System.Collections.Generic.List<System.Int32>) [], (System.Collections.Generic.List<System.Int32>) [], (System.Int32[]) [], (System.Int32[]) [], ");
        }

        [Fact]
        public void InterfaceType_03()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        IEnumerable<int> a = [1];
                        ICollection<int> b = [2];
                        IList<int> c = [3];
                        IReadOnlyCollection<int> d = [4];
                        IReadOnlyList<int> e = [5];
                        a.Report(includeType: true);
                        b.Report(includeType: true);
                        c.Report(includeType: true);
                        d.Report(includeType: true);
                        e.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensions },
                expectedOutput: "(System.Collections.Generic.List<System.Int32>) [1], (System.Collections.Generic.List<System.Int32>) [2], (System.Collections.Generic.List<System.Int32>) [3], (System.Collections.Generic.List<System.Int32>) [4], (System.Collections.Generic.List<System.Int32>) [5], ");
        }

        [Fact]
        public void NaturalType_23()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        var x = [null, 1];
                        object y = [null, 2];
                        int?[] z = [null, 3];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,17): error CS9176: There is no target type for the collection expression.
                //         var x = [null, 1];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[null, 1]").WithLocation(5, 17),
                // (6,20): error CS9174: Cannot initialize type 'object' with a collection expression because the type is not constructible.
                //         object y = [null, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[null, 2]").WithArguments("object").WithLocation(6, 20));
        }

        [Fact]
        public void NaturalType_24()
        {
            string source = """
                class Program
                {
                    static void F1(int i)
                    {
                        (string, int)[] x1 = [(null, default)];
                        string[] y1 = [i switch {  _ => default }];
                        string[] z1 = [i == 0 ? null : default];
                    }
                    static void F2(int i)
                    /*<bind>*/
                    {
                        var x2 = [(null, default)];
                        var y2 = [i switch { _ => default }];
                        var z2 = [i == 0 ? null : default];
                    }
                    /*</bind>*/
                }
                """;

            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (12,18): error CS9176: There is no target type for the collection expression.
                //         var x2 = [(null, default)];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[(null, default)]").WithLocation(12, 18),
                // (13,18): error CS9176: There is no target type for the collection expression.
                //         var y2 = [i switch { _ => default }];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[i switch { _ => default }]").WithLocation(13, 18),
                // (13,21): error CS8506: No best type was found for the switch expression.
                //         var y2 = [i switch { _ => default }];
                Diagnostic(ErrorCode.ERR_SwitchExpressionNoBestType, "switch").WithLocation(13, 21),
                // (14,18): error CS9176: There is no target type for the collection expression.
                //         var z2 = [i == 0 ? null : default];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[i == 0 ? null : default]").WithLocation(14, 18),
                // (14,19): error CS0173: Type of conditional expression cannot be determined because there is no implicit conversion between '<null>' and 'default'
                //         var z2 = [i == 0 ? null : default];
                Diagnostic(ErrorCode.ERR_InvalidQM, "i == 0 ? null : default").WithArguments("<null>", "default").WithLocation(14, 19));

            VerifyOperationTreeForTest<BlockSyntax>(comp,
                """
                IBlockOperation (3 statements, 3 locals) (OperationKind.Block, Type: null, IsInvalid) (Syntax: '{ ... }')
                  Locals: Local_1: ? x2
                    Local_2: ? y2
                    Local_3: ? z2
                  IVariableDeclarationGroupOperation (1 declarations) (OperationKind.VariableDeclarationGroup, Type: null, IsInvalid) (Syntax: 'var x2 = [( ...  default)];')
                    IVariableDeclarationOperation (1 declarators) (OperationKind.VariableDeclaration, Type: null, IsInvalid) (Syntax: 'var x2 = [( ... , default)]')
                      Declarators:
                          IVariableDeclaratorOperation (Symbol: ? x2) (OperationKind.VariableDeclarator, Type: null, IsInvalid) (Syntax: 'x2 = [(null, default)]')
                            Initializer:
                              IVariableInitializerOperation (OperationKind.VariableInitializer, Type: null, IsInvalid) (Syntax: '= [(null, default)]')
                                IOperation:  (OperationKind.None, Type: ?, IsInvalid) (Syntax: '[(null, default)]')
                                  Children(1):
                                      ITupleOperation (OperationKind.Tuple, Type: null, IsInvalid) (Syntax: '(null, default)')
                                        NaturalType: null
                                        Elements(2):
                                            ILiteralOperation (OperationKind.Literal, Type: null, Constant: null, IsInvalid) (Syntax: 'null')
                                            IDefaultValueOperation (OperationKind.DefaultValue, Type: ?, IsInvalid) (Syntax: 'default')
                      Initializer:
                        null
                  IVariableDeclarationGroupOperation (1 declarations) (OperationKind.VariableDeclarationGroup, Type: null, IsInvalid) (Syntax: 'var y2 = [i ... default }];')
                    IVariableDeclarationOperation (1 declarators) (OperationKind.VariableDeclaration, Type: null, IsInvalid) (Syntax: 'var y2 = [i ...  default }]')
                      Declarators:
                          IVariableDeclaratorOperation (Symbol: ? y2) (OperationKind.VariableDeclarator, Type: null, IsInvalid) (Syntax: 'y2 = [i swi ...  default }]')
                            Initializer:
                              IVariableInitializerOperation (OperationKind.VariableInitializer, Type: null, IsInvalid) (Syntax: '= [i switch ...  default }]')
                                IOperation:  (OperationKind.None, Type: ?, IsInvalid) (Syntax: '[i switch { ...  default }]')
                                  Children(1):
                                      ISwitchExpressionOperation (1 arms, IsExhaustive: True) (OperationKind.SwitchExpression, Type: ?, IsInvalid) (Syntax: 'i switch {  ... > default }')
                                        Value:
                                          IParameterReferenceOperation: i (OperationKind.ParameterReference, Type: System.Int32, IsInvalid) (Syntax: 'i')
                                        Arms(1):
                                            ISwitchExpressionArmOperation (0 locals) (OperationKind.SwitchExpressionArm, Type: null, IsInvalid) (Syntax: '_ => default')
                                              Pattern:
                                                IDiscardPatternOperation (OperationKind.DiscardPattern, Type: null, IsInvalid) (Syntax: '_') (InputType: System.Int32, NarrowedType: System.Int32)
                                              Value:
                                                IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: ?, IsInvalid, IsImplicit) (Syntax: 'default')
                                                  Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                                                  Operand:
                                                    IDefaultValueOperation (OperationKind.DefaultValue, Type: ?, IsInvalid) (Syntax: 'default')
                      Initializer:
                        null
                  IVariableDeclarationGroupOperation (1 declarations) (OperationKind.VariableDeclarationGroup, Type: null, IsInvalid) (Syntax: 'var z2 = [i ... : default];')
                    IVariableDeclarationOperation (1 declarators) (OperationKind.VariableDeclaration, Type: null, IsInvalid) (Syntax: 'var z2 = [i ...  : default]')
                      Declarators:
                          IVariableDeclaratorOperation (Symbol: ? z2) (OperationKind.VariableDeclarator, Type: null, IsInvalid) (Syntax: 'z2 = [i ==  ...  : default]')
                            Initializer:
                              IVariableInitializerOperation (OperationKind.VariableInitializer, Type: null, IsInvalid) (Syntax: '= [i == 0 ? ...  : default]')
                                IOperation:  (OperationKind.None, Type: ?, IsInvalid) (Syntax: '[i == 0 ? n ...  : default]')
                                  Children(1):
                                      IConditionalOperation (OperationKind.Conditional, Type: ?, IsInvalid) (Syntax: 'i == 0 ? null : default')
                                        Condition:
                                          IBinaryOperation (BinaryOperatorKind.Equals) (OperationKind.Binary, Type: System.Boolean, IsInvalid) (Syntax: 'i == 0')
                                            Left:
                                              IParameterReferenceOperation: i (OperationKind.ParameterReference, Type: System.Int32, IsInvalid) (Syntax: 'i')
                                            Right:
                                              ILiteralOperation (OperationKind.Literal, Type: System.Int32, Constant: 0, IsInvalid) (Syntax: '0')
                                        WhenTrue:
                                          IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: ?, Constant: null, IsInvalid, IsImplicit) (Syntax: 'null')
                                            Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: True, IsUserDefined: False) (MethodSymbol: null)
                                            Operand:
                                              ILiteralOperation (OperationKind.Literal, Type: null, Constant: null, IsInvalid) (Syntax: 'null')
                                        WhenFalse:
                                          IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: ?, IsInvalid, IsImplicit) (Syntax: 'default')
                                            Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                                            Operand:
                                              IDefaultValueOperation (OperationKind.DefaultValue, Type: ?, IsInvalid) (Syntax: 'default')
                      Initializer:
                        null
                """);
        }

        [Fact]
        public void TargetType_01()
        {
            string source = """
                class Program
                {
                    static void F(bool b, object o)
                    {
                        int[] a1 = b ? [1] : [];
                        int[] a2 = b? [] : [2];
                        object[] a3 = b ? [3] : [o];
                        object[] a4 = b ? [o] : [4];
                        int?[] a5 = b ? [5] : [null];
                        int?[] a6 = b ? [null] : [6];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();
        }

        [Fact]
        public void TargetType_02()
        {
            string source = """
                using System;
                class Program
                {
                    static void F(bool b, object o)
                    {
                        Func<int[]> f1 = () => { if (b) return [1]; return []; };
                        Func<int[]> f2 = () => { if (b) return []; return [2]; };
                        Func<object[]> f3 = () => { if (b) return [3]; return [o]; };
                        Func<object[]> f4 = () => { if (b) return [o]; return [4]; };
                        Func<int?[]> f5 = () => { if (b) return [5]; return [null]; };
                        Func<int?[]> f6 = () => { if (b) return [null]; return [6]; };
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();
        }

        // Overload resolution should choose array over interface.
        [Fact]
        public void OverloadResolution_01()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static IEnumerable<int> F1(IEnumerable<int> arg) => arg;
                    static int[] F1(int[] arg) => arg;
                    static int[] F2(int[] arg) => arg;
                    static IEnumerable<int> F2(IEnumerable<int> arg) => arg;
                    static void Main()
                    {
                        var x = F1([]);
                        var y = F2([1, 2]);
                        x.Report(includeType: true);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [], (System.Int32[]) [1, 2], ");
        }

        // Overload resolution should choose collection initializer type over interface.
        [Fact]
        public void OverloadResolution_02()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static IEnumerable<int> F1(IEnumerable<int> arg) => arg;
                    static List<int> F1(List<int> arg) => arg;
                    static List<int> F2(List<int> arg) => arg;
                    static IEnumerable<int> F2(IEnumerable<int> arg) => arg;
                    static void Main()
                    {
                        var x = F1([]);
                        var y = F2([1, 2]);
                        x.Report(includeType: true);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Collections.Generic.List<System.Int32>) [], (System.Collections.Generic.List<System.Int32>) [1, 2], ");
        }

        [Fact]
        public void OverloadResolution_03()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static List<int> F(List<int> arg) => arg;
                    static int[] F(int[] arg) => arg;
                    static void Main()
                    {
                        var x = F([]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (8,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F(List<int>)' and 'Program.F(int[])'
                //         var x = F([]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F").WithArguments("Program.F(System.Collections.Generic.List<int>)", "Program.F(int[])").WithLocation(8, 17));
        }

        [Fact]
        public void OverloadResolution_04()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static List<int> F1(List<int> arg) => arg;
                    static int[] F1(int[] arg) => arg;
                    static int[] F2(int[] arg) => arg;
                    static List<int> F2(List<int> arg) => arg;
                    static void Main()
                    {
                        var x = F1([1]);
                        var y = F2([2]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (10,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F1(List<int>)' and 'Program.F1(int[])'
                //         var x = F1([1]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F1").WithArguments("Program.F1(System.Collections.Generic.List<int>)", "Program.F1(int[])").WithLocation(10, 17),
                // (11,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F2(int[])' and 'Program.F2(List<int>)'
                //         var y = F2([2]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F2").WithArguments("Program.F2(int[])", "Program.F2(System.Collections.Generic.List<int>)").WithLocation(11, 17));
        }

        [Fact]
        public void OverloadResolution_05()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static List<int> F1(List<int> arg) => arg;
                    static List<long?> F1(List<long?> arg) => arg;
                    static List<long?> F2(List<long?> arg) => arg;
                    static List<int> F2(List<int> arg) => arg;
                    static void Main()
                    {
                        var x = F1([1]);
                        var y = F2([2]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (10,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F1(List<int>)' and 'Program.F1(List<long?>)'
                //         var x = F1([1]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F1").WithArguments("Program.F1(System.Collections.Generic.List<int>)", "Program.F1(System.Collections.Generic.List<long?>)").WithLocation(10, 17),
                // (11,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F2(List<long?>)' and 'Program.F2(List<int>)'
                //         var y = F2([2]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F2").WithArguments("Program.F2(System.Collections.Generic.List<long?>)", "Program.F2(System.Collections.Generic.List<int>)").WithLocation(11, 17));
        }

        [Fact]
        public void OverloadResolution_06()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static List<int?> F1(List<int?> arg) => arg;
                    static List<long> F1(List<long> arg) => arg;
                    static List<long> F2(List<long> arg) => arg;
                    static List<int?> F2(List<int?> arg) => arg;
                    static void Main()
                    {
                        var x = F1([1]);
                        var y = F2([2]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // 0.cs(10,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F1(List<int?>)' and 'Program.F1(List<long>)'
                //         var x = F1([1]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F1").WithArguments("Program.F1(System.Collections.Generic.List<int?>)", "Program.F1(System.Collections.Generic.List<long>)").WithLocation(10, 17),
                // 0.cs(11,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F2(List<long>)' and 'Program.F2(List<int?>)'
                //         var y = F2([2]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F2").WithArguments("Program.F2(System.Collections.Generic.List<long>)", "Program.F2(System.Collections.Generic.List<int?>)").WithLocation(11, 17));
        }

        [Fact]
        public void OverloadResolution_07()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                struct S : IEnumerable
                {
                    IEnumerator IEnumerable.GetEnumerator() => null;
                    public void Add(int i) { }
                }
                class Program
                {
                    static S F1(S arg) => arg;
                    static List<int> F1(List<int> arg) => arg;
                    static List<int> F2(List<int> arg) => arg;
                    static S F2(S arg) => arg;
                    static void Main()
                    {
                        var x = F1([1]);
                        var y = F2([2]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (16,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F1(S)' and 'Program.F1(List<int>)'
                //         var x = F1([1]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F1").WithArguments("Program.F1(S)", "Program.F1(System.Collections.Generic.List<int>)").WithLocation(16, 17),
                // (17,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F2(List<int>)' and 'Program.F2(S)'
                //         var y = F2([2]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F2").WithArguments("Program.F2(System.Collections.Generic.List<int>)", "Program.F2(S)").WithLocation(17, 17));
        }

        [Fact]
        public void OverloadResolution_08()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static IEnumerable<T> F1<T>(IEnumerable<T> arg) => arg;
                    static T[] F1<T>(T[] arg) => arg;
                    static T[] F2<T>(T[] arg) => arg;
                    static IEnumerable<T> F2<T>(IEnumerable<T> arg) => arg;
                    static void Main()
                    {
                        var x = F1([1]);
                        var y = F2([2]);
                        x.Report(includeType: true);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [1], (System.Int32[]) [2], ");
        }

        [Fact]
        public void OverloadResolution_09()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static int[] F1(int[] arg) => arg;
                    static string[] F1(string[] arg) => arg;
                    static List<int> F2(List<int> arg) => arg;
                    static List<string> F2(List<string> arg) => arg;
                    static string[] F3(string[] arg) => arg;
                    static List<int?> F3(List<int?> arg) => arg;
                    static void Main()
                    {
                        F1([]);
                        F2([]);
                        F3([null]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (12,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F1(int[])' and 'Program.F1(string[])'
                //         F1([]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F1").WithArguments("Program.F1(int[])", "Program.F1(string[])").WithLocation(12, 9),
                // (13,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F2(List<int>)' and 'Program.F2(List<string>)'
                //         F2([]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F2").WithArguments("Program.F2(System.Collections.Generic.List<int>)", "Program.F2(System.Collections.Generic.List<string>)").WithLocation(13, 9),
                // (14,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F3(string[])' and 'Program.F3(List<int?>)'
                //         F3([null]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F3").WithArguments("Program.F3(string[])", "Program.F3(System.Collections.Generic.List<int?>)").WithLocation(14, 9));
        }

        [Fact]
        public void OverloadResolution_ArgumentErrors()
        {
            string source = """
                using System.Linq;
                class Program
                {
                    static void Main()
                    {
                        [Unknown2].Zip([Unknown1]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,10): error CS0103: The name 'Unknown2' does not exist in the current context
                //         [Unknown2].Zip([Unknown1]);
                Diagnostic(ErrorCode.ERR_NameNotInContext, "Unknown2").WithArguments("Unknown2").WithLocation(6, 10),
                // (6,25): error CS0103: The name 'Unknown1' does not exist in the current context
                //         [Unknown2].Zip([Unknown1]);
                Diagnostic(ErrorCode.ERR_NameNotInContext, "Unknown1").WithArguments("Unknown1").WithLocation(6, 25));
        }

        private const string example_RefStructCollection = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(RefStructCollectionBuilder), nameof(RefStructCollectionBuilder.Create))]
                ref struct RefStructCollection<T>
                {
                    public IEnumerator<T> GetEnumerator() => null;
                }
                static class RefStructCollectionBuilder
                {
                    public static RefStructCollection<T> Create<T>(scoped ReadOnlySpan<T> items) => default;
                }
                """;

        private const string example_GenericClassCollection = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(GenericClassCollectionBuilder), nameof(GenericClassCollectionBuilder.Create))]
                class GenericClassCollection<T>
                {
                    public IEnumerator<T> GetEnumerator() => null;
                }
                static class GenericClassCollectionBuilder
                {
                    public static GenericClassCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;

        private const string example_NonGenericClassCollection = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(NonGenericClassCollectionBuilder), nameof(NonGenericClassCollectionBuilder.Create))]
                class NonGenericClassCollection
                {
                    public IEnumerator<object> GetEnumerator() => null;
                }
                static class NonGenericClassCollectionBuilder
                {
                    public static NonGenericClassCollection Create(ReadOnlySpan<object> items) => default;
                }
                """;

        [Theory]
        [InlineData("System.Span<T>", "T[]", "System.Span<System.Int32>")]
        [InlineData("System.Span<T>", "System.Collections.Generic.IEnumerable<T>", "System.Span<System.Int32>")]
        [InlineData("System.Span<T>", "System.Collections.Generic.IReadOnlyCollection<T>", "System.Span<System.Int32>")]
        [InlineData("System.Span<T>", "System.Collections.Generic.IReadOnlyList<T>", "System.Span<System.Int32>")]
        [InlineData("System.Span<T>", "System.Collections.Generic.ICollection<T>", "System.Span<System.Int32>")]
        [InlineData("System.Span<T>", "System.Collections.Generic.IList<T>", "System.Span<System.Int32>")]
        [InlineData("System.Span<T>", "System.Collections.Generic.HashSet<T>", "System.Span<System.Int32>")]
        [InlineData("System.Span<T>", "System.ReadOnlySpan<object>", null)] // rule requires ref struct and non- ref struct
        [InlineData("RefStructCollection<T>", "T[]", "RefStructCollection<System.Int32>", new[] { example_RefStructCollection })]
        [InlineData("RefStructCollection<T>", "RefStructCollection<object>", null, new[] { example_RefStructCollection })] // rule requires ref struct and non- ref struct
        [InlineData("RefStructCollection<int>", "GenericClassCollection<object>", "RefStructCollection<System.Int32>", new[] { example_RefStructCollection, example_GenericClassCollection })]
        [InlineData("RefStructCollection<object>", "GenericClassCollection<int>", null, new[] { example_RefStructCollection, example_GenericClassCollection })] // cannot convert object to int
        [InlineData("RefStructCollection<int>", "NonGenericClassCollection", "RefStructCollection<System.Int32>", new[] { example_RefStructCollection, example_NonGenericClassCollection })]
        [InlineData("GenericClassCollection<T>", "T[]", null, new[] { example_GenericClassCollection })] // rule requires ref struct
        [InlineData("NonGenericClassCollection", "object[]", null, new[] { example_NonGenericClassCollection })] // rule requires ref struct
        [InlineData("System.ReadOnlySpan<T>", "object[]", "System.ReadOnlySpan<System.Int32>")]
        [InlineData("System.ReadOnlySpan<T>", "long[]", "System.ReadOnlySpan<System.Int32>")]
        [InlineData("System.ReadOnlySpan<T>", "short[]", null)] // cannot convert int to short
        [InlineData("System.ReadOnlySpan<long>", "T[]", null)] // cannot convert long to int
        [InlineData("System.ReadOnlySpan<object>", "long[]", null)] // cannot convert object to long
        [InlineData("System.ReadOnlySpan<long>", "object[]", "System.ReadOnlySpan<System.Int64>")]
        [InlineData("System.ReadOnlySpan<long>", "string[]", null)] // https://github.com/dotnet/roslyn/issues/69634: should use System.ReadOnlySpan<System.Int64>
        [InlineData("System.ReadOnlySpan<T>", "System.Span<T>", "System.Span<System.Int32>")] // implicit conversion from Span<T> to ReadOnlySpan<T>
        [InlineData("System.ReadOnlySpan<T>", "System.Span<int>", "System.Span<System.Int32>")]
        [InlineData("System.ReadOnlySpan<T>", "System.ReadOnlySpan<object>", null)] // cannot convert between ReadOnlySpan<int> and ReadOnlySpan<object>
        [InlineData("System.ReadOnlySpan<T>", "System.ReadOnlySpan<long>", null)] // cannot convert between ReadOnlySpan<int> and ReadOnlySpan<long>
        [InlineData("System.ReadOnlySpan<object>", "System.ReadOnlySpan<long>", null)] // cannot convert between ReadOnlySpan<object> and ReadOnlySpan<long>
        [InlineData("System.ReadOnlySpan<int>", "System.ReadOnlySpan<string>", null)] // https://github.com/dotnet/roslyn/issues/69634: should use System.ReadOnlySpan<System.Int32>
        [InlineData("System.Span<int>", "int?[]", "System.Span<System.Int32>")]
        [InlineData("System.Span<int?>", "int[]", null)] // cannot convert int? to int
        [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.IEnumerable<int>", "System.Collections.Generic.List<System.Int32>")]
        [InlineData("int[]", "object[]", null)] // rule requires ref struct
        [InlineData("int[]", "System.Collections.Generic.IReadOnlyList<object>", null)] // rule requires ref struct
        public void BetterConversionFromExpression_01(string type1, string type2, string expectedType, string[] additionalSources = null)
        {
            string source = $$"""
                using System;
                class Program
                {
                    {{generateMethod("F1", type1)}}
                    {{generateMethod("F1", type2)}}
                    {{generateMethod("F2", type2)}}
                    {{generateMethod("F2", type1)}}
                    static void Main()
                    {
                        var x = F1([1, 2, 3]);
                        Console.WriteLine(x.GetTypeName());
                        var y = F2([4, 5]);
                        Console.WriteLine(y.GetTypeName());
                    }
                }
                """;
            var comp = CreateCompilation(
                getSources(source, additionalSources),
                targetFramework: TargetFramework.Net80,
                options: TestOptions.ReleaseExe);
            if (expectedType is { })
            {
                CompileAndVerify(comp, verify: Verification.Skipped, expectedOutput: IncludeExpectedOutput($"""
                    {expectedType}
                    {expectedType}
                    """));
            }
            else
            {
                comp.VerifyEmitDiagnostics(
                    // 0.cs(10,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F1(ReadOnlySpan<long>)' and 'Program.F1(ReadOnlySpan<object>)'
                    //         var x = F1([1, 2, 3]);
                    Diagnostic(ErrorCode.ERR_AmbigCall, "F1").WithArguments(generateMethodSignature("F1", type1), generateMethodSignature("F1", type2)).WithLocation(10, 17),
                    // 0.cs(12,17): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F2(ReadOnlySpan<object>)' and 'Program.F2(ReadOnlySpan<long>)'
                    //         var y = F2([4, 5]);
                    Diagnostic(ErrorCode.ERR_AmbigCall, "F2").WithArguments(generateMethodSignature("F2", type2), generateMethodSignature("F2", type1)).WithLocation(12, 17));
            }

            static string getTypeParameters(string type) =>
                type.Contains("T[]") || type.Contains("<T>") ? "<T>" : "";

            static string generateMethod(string methodName, string parameterType) =>
                $"static Type {methodName}{getTypeParameters(parameterType)}({parameterType} value) => typeof({parameterType});";

            static string generateMethodSignature(string methodName, string parameterType) =>
                $"Program.{methodName}{getTypeParameters(parameterType)}({parameterType})";

            static string[] getSources(string source, string[] additionalSources)
            {
                var builder = ArrayBuilder<string>.GetInstance();
                builder.Add(source);
                builder.Add(s_collectionExtensions);
                if (additionalSources is { }) builder.AddRange(additionalSources);
                return builder.ToArrayAndFree();
            }
        }

        [Fact]
        public void BetterConversionFromExpression_02()
        {
            string sourceA = """
                using System;
                using static System.Console;

                partial class Program
                {
                    static void Generic<T>(Span<T> value) { WriteLine("Span<T>"); }
                    static void Generic<T>(T[] value)     { WriteLine("T[]"); }

                    static void Identical(Span<string> value) { WriteLine("Span<string>"); }
                    static void Identical(string[] value)     { WriteLine("string[]"); }

                    static void SpanDerived(Span<string> value) { WriteLine("Span<string>"); }
                    static void SpanDerived(object[] value)     { WriteLine("object[]"); }

                    static void ArrayDerived(Span<object> value) { WriteLine("Span<object>"); }
                    static void ArrayDerived(string[] value)     { WriteLine("string[]"); }
                }
                """;

            string sourceB1 = """
                partial class Program
                {
                    static void Main()
                    {
                        Generic(new[] { string.Empty }); // string[]
                        Identical(new[] { string.Empty }); // string[]
                        ArrayDerived(new[] { string.Empty }); // string[]

                        Generic([string.Empty]); // Span<string>
                        Identical([string.Empty]); // Span<string>
                        SpanDerived([string.Empty]); // Span<string>
                    }
                }
                """;
            var comp = CreateCompilation(
                new[] { sourceA, sourceB1 },
                targetFramework: TargetFramework.Net80,
                options: TestOptions.ReleaseExe);
            CompileAndVerify(comp, verify: Verification.Skipped, expectedOutput: IncludeExpectedOutput("""
                T[]
                string[]
                string[]
                Span<T>
                Span<string>
                Span<string>
                """));

            string sourceB2 = """
                partial class Program
                {
                    static void Main()
                    {
                        SpanDerived(new[] { string.Empty }); // ambiguous
                        ArrayDerived([string.Empty]); // ambiguous
                    }
                }
                """;
            comp = CreateCompilation(
                new[] { sourceA, sourceB2 },
                targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 1.cs(5,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.SpanDerived(Span<string>)' and 'Program.SpanDerived(object[])'
                //         SpanDerived(new[] { string.Empty }); // ambiguous
                Diagnostic(ErrorCode.ERR_AmbigCall, "SpanDerived").WithArguments("Program.SpanDerived(System.Span<string>)", "Program.SpanDerived(object[])").WithLocation(5, 9),
                // 1.cs(6,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.ArrayDerived(Span<object>)' and 'Program.ArrayDerived(string[])'
                //         ArrayDerived([string.Empty]); // ambiguous
                Diagnostic(ErrorCode.ERR_AmbigCall, "ArrayDerived").WithArguments("Program.ArrayDerived(System.Span<object>)", "Program.ArrayDerived(string[])").WithLocation(6, 9));
        }

        [Fact]
        public void BetterConversionFromExpression_03()
        {
            string sourceA = """
                using System;
                using static System.Console;

                partial class Program
                {
                    static void Unrelated(Span<int> value) { WriteLine("Span<int>"); }
                    static void Unrelated(string[] value)     { WriteLine("string[]"); }
                }
                """;

            string sourceB1 = """
                partial class Program
                {
                    static void Main()
                    {
                        Unrelated(new[] { 1 }); // Span<int>
                        Unrelated(new[] { string.Empty }); // string[]

                        Unrelated([2]); // Span<string>
                        Unrelated([string.Empty]); // string[]
                    }
                }
                """;
            var comp = CreateCompilation(
                new[] { sourceA, sourceB1 },
                targetFramework: TargetFramework.Net80,
                options: TestOptions.ReleaseExe);
            // https://github.com/dotnet/roslyn/issues/69634: Should use Span<int>, string[], Span<int>, string[]
            comp.VerifyEmitDiagnostics(
                // 1.cs(8,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.Unrelated(Span<int>)' and 'Program.Unrelated(string[])'
                //         Unrelated([2]); // Span<string>
                Diagnostic(ErrorCode.ERR_AmbigCall, "Unrelated").WithArguments("Program.Unrelated(System.Span<int>)", "Program.Unrelated(string[])").WithLocation(8, 9),
                // 1.cs(9,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.Unrelated(Span<int>)' and 'Program.Unrelated(string[])'
                //         Unrelated([string.Empty]); // string[]
                Diagnostic(ErrorCode.ERR_AmbigCall, "Unrelated").WithArguments("Program.Unrelated(System.Span<int>)", "Program.Unrelated(string[])").WithLocation(9, 9));

            string sourceB2 = """
                partial class Program
                {
                    static void Main()
                    {
                        Unrelated(new[] { default }); // error
                        Unrelated([default]); // ambiguous
                    }
                }
                """;
            comp = CreateCompilation(
                new[] { sourceA, sourceB2 },
                targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 1.cs(5,19): error CS0826: No best type found for implicitly-typed array
                //         Unrelated(new[] { default }); // error
                Diagnostic(ErrorCode.ERR_ImplicitlyTypedArrayNoBestType, "new[] { default }").WithLocation(5, 19),
                // 1.cs(5,19): error CS1503: Argument 1: cannot convert from '?[]' to 'System.Span<int>'
                //         Unrelated(new[] { default }); // error
                Diagnostic(ErrorCode.ERR_BadArgType, "new[] { default }").WithArguments("1", "?[]", "System.Span<int>").WithLocation(5, 19),
                // 1.cs(6,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.Unrelated(Span<int>)' and 'Program.Unrelated(string[])'
                //         Unrelated([default]); // ambiguous
                Diagnostic(ErrorCode.ERR_AmbigCall, "Unrelated").WithArguments("Program.Unrelated(System.Span<int>)", "Program.Unrelated(string[])").WithLocation(6, 9));
        }

        [Fact]
        public void BetterConversionFromExpression_04()
        {
            string source = """
                using System;
                class Program
                {
                    static void F1(int[] x, int[] y) { throw null; }
                    static void F1(Span<object> x, ReadOnlySpan<int> y) { }
                    static void F2(object x, string[] y) { throw null; }
                    static void F2(string x, Span<object> y) { }
                    static void Main()
                    {
                        F1([1], [2]);
                        F2("3", ["4"]);
                    }
                }
                """;
            CompileAndVerify(
                source,
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput(""));
        }

        [Fact]
        public void BetterConversionFromExpression_05()
        {
            string source = """
                using System;
                class Program
                {
                    static void F1(Span<int> x, int[] y) { throw null; }
                    static void F1(int[] x, ReadOnlySpan<int> y) { }
                    static void F2(string x, string[] y) { throw null; }
                    static void F2(object x, Span<string> y) { }
                    static void Main()
                    {
                        F1([1], [2]);
                        F2("3", ["4"]);
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (10,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F1(Span<int>, int[])' and 'Program.F1(int[], ReadOnlySpan<int>)'
                //         F1([1], [2]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F1").WithArguments("Program.F1(System.Span<int>, int[])", "Program.F1(int[], System.ReadOnlySpan<int>)").WithLocation(10, 9),
                // (11,9): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F2(string, string[])' and 'Program.F2(object, Span<string>)'
                //         F2("3", ["4"]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F2").WithArguments("Program.F2(string, string[])", "Program.F2(object, System.Span<string>)").WithLocation(11, 9));
        }

        // Two ref struct collection types, with an implicit conversion from one to the other.
        [Fact]
        public void BetterConversionFromExpression_06()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create1))]
                ref struct MyCollection1<T>
                {
                    private readonly List<T> _list;
                    public MyCollection1(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    public static implicit operator MyCollection2<T>(MyCollection1<T> c) => new(c._list);
                }
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create2))]
                ref struct MyCollection2<T>
                {
                    private readonly List<T> _list;
                    public MyCollection2(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                }
                static class MyCollectionBuilder
                {
                    public static MyCollection1<T> Create1<T>(scoped ReadOnlySpan<T> items)
                    {
                        return new MyCollection1<T>(new List<T>(items.ToArray()));
                    }
                    public static MyCollection2<T> Create2<T>(scoped ReadOnlySpan<T> items)
                    {
                        return new MyCollection2<T>(new List<T>(items.ToArray()));
                    }
                }
                class Program
                {
                    static void F1<T>(MyCollection1<T> c) { Console.WriteLine("MyCollection1<T>"); }
                    static void F1<T>(MyCollection2<T> c) { Console.WriteLine("MyCollection2<T>"); }
                    static void F2(MyCollection2<object> c) { Console.WriteLine("MyCollection2<object>"); }
                    static void F2(MyCollection1<object> c) { Console.WriteLine("MyCollection1<object>"); }
                    static void Main()
                    {
                        F1([1, 2, 3]);
                        F2([4, null]);
                        F1((MyCollection1<object>)[6]);
                        F1((MyCollection2<int>)[7]);
                        F2((MyCollection2<object>)[8]);
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, CollectionBuilderAttributeDefinition },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("""
                    MyCollection1<T>
                    MyCollection1<object>
                    MyCollection1<T>
                    MyCollection2<T>
                    MyCollection2<object>
                    """));
        }

        [Fact]
        public void BestCommonType_01()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        var x = new[] { new int[0], [1, 2, 3] };
                        x.Report(includeType: true);
                        var y = new[] { new[] { new int[0] }, [[1, 2, 3]] };
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[][]) [[], [1, 2, 3]], (System.Int32[][][]) [[[]], [[1, 2, 3]]], ");
        }

        [Fact]
        public void BestCommonType_02()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        var x = new[] { new byte[0], [1, 2, 3] };
                        x.Report(includeType: true);
                        var y = new[] { new[] { new byte[0] }, [[1, 2, 3]] };
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Byte[][]) [[], [1, 2, 3]], (System.Byte[][][]) [[[]], [[1, 2, 3]]], ");
        }

        [Fact]
        public void BestCommonType_03()
        {
            string source = """
                class Program
                {
                    static void Main(string[] args)
                    {
                        var x = new[] { [""], new object[0] };
                        var y = new[] { [[""]], [new object[0]] };
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,17): error CS0826: No best type found for implicitly-typed array
                //         var y = new[] { [[""]], [new object[0]] };
                Diagnostic(ErrorCode.ERR_ImplicitlyTypedArrayNoBestType, @"new[] { [[""""]], [new object[0]] }").WithLocation(6, 17));
        }

        [Fact]
        public void BestCommonType_04()
        {
            string source = """
                class Program
                {
                    static void Main(string[] args)
                    {
                        var x = args.Length > 0 ? new int[0] : [1, 2, 3];
                        x.Report(includeType: true);
                        var y = args.Length == 0 ? [[4, 5]] : new[] { new byte[0] };
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [1, 2, 3], (System.Byte[][]) [[4, 5]], ");
        }

        [Fact]
        public void BestCommonType_05()
        {
            string source = """
                class Program
                {
                    static void Main(string[] args)
                    {
                        bool b = args.Length > 0;
                        var y = b ? [new int[0]] : [[1, 2, 3]];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,17): error CS0173: Type of conditional expression cannot be determined because there is no implicit conversion between 'collection expressions' and 'collection expressions'
                //         var y = b ? [new int[0]] : [[1, 2, 3]];
                Diagnostic(ErrorCode.ERR_InvalidQM, "b ? [new int[0]] : [[1, 2, 3]]").WithArguments("collection expressions", "collection expressions").WithLocation(6, 17));
        }

        [Fact]
        public void TypeInference_01()
        {
            string source = """
                static class Program
                {
                    static T F<T>(T a, T b)
                    {
                        return b;
                    }
                    static void Main()
                    {
                        var x = F(["str"]);
                        var y = F([[], [1, 2]]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (9,17): error CS7036: There is no argument given that corresponds to the required parameter 'b' of 'Program.F<T>(T, T)'
                //         var x = F(["str"]);
                Diagnostic(ErrorCode.ERR_NoCorrespondingArgument, "F").WithArguments("b", "Program.F<T>(T, T)").WithLocation(9, 17),
                // (10,17): error CS7036: There is no argument given that corresponds to the required parameter 'b' of 'Program.F<T>(T, T)'
                //         var y = F([[], [1, 2]]);
                Diagnostic(ErrorCode.ERR_NoCorrespondingArgument, "F").WithArguments("b", "Program.F<T>(T, T)").WithLocation(10, 17));
        }

        [Fact]
        public void TypeInference_02()
        {
            string source = """
                static class Program
                {
                    static T F<T>(T a, T b)
                    {
                        return b;
                    }
                    static void Main()
                    {
                        _ = F([new int[0]], new[] { [1, 2, 3] });
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (9,29): error CS0826: No best type found for implicitly-typed array
                //         _ = F([new int[0]], new[] { [1, 2, 3] });
                Diagnostic(ErrorCode.ERR_ImplicitlyTypedArrayNoBestType, "new[] { [1, 2, 3] }").WithLocation(9, 29));
        }

        [Fact]
        public void TypeInference_03()
        {
            string source = """
                class Program
                {
                    static T[] AsArray1<T>(T[] args) => args;
                    static T[] AsArray2<T>(params T[] args) => args;
                    static void Main()
                    {
                        var a = AsArray1([1, 2, 3]);
                        a.Report();
                        var b = AsArray2(["4", null]);
                        b.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2, 3], [4, null], ");
        }

        [Fact]
        public void TypeInference_04()
        {
            string source = """
                class Program
                {
                    static T[] AsArray<T>(T[] args)
                    {
                        return args;
                    }
                    static void Main()
                    {
                        AsArray([]);
                        AsArray([1, null]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (9,9): error CS0411: The type arguments for method 'Program.AsArray<T>(T[])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         AsArray([]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "AsArray").WithArguments("Program.AsArray<T>(T[])").WithLocation(9, 9),
                // (10,21): error CS0037: Cannot convert null to 'int' because it is a non-nullable value type
                //         AsArray([1, null]);
                Diagnostic(ErrorCode.ERR_ValueCantBeNull, "null").WithArguments("int").WithLocation(10, 21));
        }

        [Fact]
        public void TypeInference_06()
        {
            string source = """
                class Program
                {
                    static T[] AsArray<T>(T[] args)
                    {
                        return args;
                    }
                    static void F(bool b, int x, int y)
                    {
                        var a = AsArray([.. b ? [x] : [y]]);
                        a.Report();
                    }
                    static void Main()
                    {
                        F(false, 1, 2);
                    }
                }
                """;
            var comp = CreateCompilation(new[] { source, s_collectionExtensions });
            comp.VerifyEmitDiagnostics(
                // 0.cs(9,29): error CS0173: Type of conditional expression cannot be determined because there is no implicit conversion between 'collection expressions' and 'collection expressions'
                //         var a = AsArray([.. b ? [x] : [y]]);
                Diagnostic(ErrorCode.ERR_InvalidQM, "b ? [x] : [y]").WithArguments("collection expressions", "collection expressions").WithLocation(9, 29));
        }

        [Fact]
        public void TypeInference_07()
        {
            string source = """
                static class Program
                {
                    static T[] AsArray<T>(this T[] args)
                    {
                        return args;
                    }
                    static void Main()
                    {
                        var a = [1, 2, 3].AsArray();
                        a.Report();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { source, s_collectionExtensions });
            comp.VerifyEmitDiagnostics(
                // 0.cs(9,17): error CS9176: There is no target type for the collection expression.
                //         var a = [1, 2, 3].AsArray();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[1, 2, 3]").WithLocation(9, 17));
        }

        [Fact]
        public void TypeInference_08()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                struct S<T> : IEnumerable<T>
                {
                    private List<T> _list;
                    public void Add(T t)
                    {
                        _list ??= new List<T>();
                        _list.Add(t);
                    }
                    public IEnumerator<T> GetEnumerator()
                    {
                        _list ??= new List<T>();
                        return _list.GetEnumerator();
                    }
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                static class Program
                {
                    static S<T> AsCollection<T>(this S<T> args)
                    {
                        return args;
                    }
                    static void Main()
                    {
                        var a = AsCollection([1, 2, 3]);
                        var b = [4].AsCollection();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (27,17): error CS9176: There is no target type for the collection expression.
                //         var b = [4].AsCollection();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[4]").WithLocation(27, 17));
        }

        [Fact]
        public void TypeInference_09()
        {
            string source = """
                using System.Collections;
                struct S<T> : IEnumerable
                {
                    public void Add(T t) { }
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }
                static class Program
                {
                    static S<T> AsCollection<T>(this S<T> args)
                    {
                        return args;
                    }
                    static void Main()
                    {
                        _ = AsCollection([1, 2, 3]);
                        _ = [4].AsCollection();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (15,13): error CS0411: The type arguments for method 'Program.AsCollection<T>(S<T>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         _ = AsCollection([1, 2, 3]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "AsCollection").WithArguments("Program.AsCollection<T>(S<T>)").WithLocation(15, 13),
                // (16,13): error CS9176: There is no target type for the collection expression.
                //         _ = [4].AsCollection();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[4]").WithLocation(16, 13));
        }

        [Fact]
        public void TypeInference_10()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static T[] F<T>(T[] arg) => arg;
                    static List<T> F<T>(List<T> arg) => arg;
                    static void Main()
                    {
                        _ = F([1, 2, 3]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (8,13): error CS0121: The call is ambiguous between the following methods or properties: 'Program.F<T>(T[])' and 'Program.F<T>(List<T>)'
                //         _ = F([1, 2, 3]);
                Diagnostic(ErrorCode.ERR_AmbigCall, "F").WithArguments("Program.F<T>(T[])", "Program.F<T>(System.Collections.Generic.List<T>)").WithLocation(8, 13));
        }

        [Fact]
        public void TypeInference_11()
        {
            string source = """
                using System.Collections;
                struct S<T> : IEnumerable
                {
                    public void Add(T t) { }
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                class Program
                {
                    static T[] F<T>(T[] arg) => arg;
                    static S<T> F<T>(S<T> arg) => arg;
                    static void Main()
                    {
                        var x = F([1, 2, 3]);
                        x.Report(true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [1, 2, 3], ");
        }

        [Fact]
        public void TypeInference_12()
        {
            string source = """
                class Program
                {
                    static T[] F<T>(T[] x, T[] y) => x;
                    static void Main()
                    {
                        var x = F(["1"], [(object)"2"]);
                        x.Report(includeType: true);
                        var y = F([(object)"3"], ["4"]);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Object[]) [1], (System.Object[]) [3], ");
        }

        [Fact]
        public void TypeInference_13A()
        {
            string source = """
                class Program
                {
                    static T[] F<T>(T[] x, T[] y) => x;
                    static void Main()
                    {
                        var x = F([1], [(long)2]);
                        x.Report(includeType: true);
                        var y = F([(long)3], [4]);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int64[]) [1], (System.Int64[]) [3], ");
        }

        [Fact]
        public void TypeInference_13B()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static HashSet<T> F<T>(HashSet<T> x, HashSet<T> y) => x;
                    static void Main()
                    {
                        var x = F([1], [(long)2]);
                        x.Report(includeType: true);
                        var y = F([(long)3], [4]);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Collections.Generic.HashSet<System.Int64>) [1], (System.Collections.Generic.HashSet<System.Int64>) [3], ");
        }

        [Fact]
        public void TypeInference_14()
        {
            string source = """
                class Program
                {
                    static T[] F<T>(T[][] x) => x[0];
                    static void Main()
                    {
                        var x = F([[1, 2, 3]]);
                        x.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [1, 2, 3], ");
        }

        [Fact]
        public void TypeInference_15()
        {
            string source = """
                class Program
                {
                    static T F0<T>(T[] x, T y) => y;
                    static T[] F1<T>(T[] x, T[] y) => y;
                    static T[] F2<T>(T[][] x, T[][] y) => y[0];
                    static void Main()
                    {
                        var x = F0(new byte[0], 1);
                        var y = F1(new byte[0], [1, 2]);
                        var z = F2(new[] { new byte[0] }, [[3, 4]]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (8,17): error CS0411: The type arguments for method 'Program.F0<T>(T[], T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x = F0(new byte[0], 1);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F0").WithArguments("Program.F0<T>(T[], T)").WithLocation(8, 17),
                // (9,17): error CS0411: The type arguments for method 'Program.F1<T>(T[], T[])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var y = F1(new byte[0], [1, 2]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1").WithArguments("Program.F1<T>(T[], T[])").WithLocation(9, 17),
                // (10,17): error CS0411: The type arguments for method 'Program.F2<T>(T[][], T[][])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var z = F2(new[] { new byte[0] }, [[3, 4]]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F2").WithArguments("Program.F2<T>(T[][], T[][])").WithLocation(10, 17));
        }

        [Fact]
        public void TypeInference_16()
        {
            string source = """
                class Program
                {
                    static T[] F1<T>(T[] x, T[] y) => y;
                    static T[] F2<T>(T[][] x, T[][] y) => y[0];
                    static void Main()
                    {
                        var x = F1([1], [(byte)2]);
                        x.Report(true);
                        var y = F2([[3]], [[(byte)4]]);
                        y.Report(true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [2], (System.Int32[]) [4], ");
        }

        [Fact]
        public void TypeInference_17()
        {
            string source = """
                class Program
                {
                    static T[] F1<T>(T[] x, T[] y) => y;
                    static T[] F2<T>(T[][] x, T[][] y) => y[0];
                    static void Main()
                    {
                        var x = F1([(long)1], [(int?)2]);
                        var y = F2([[(int?)3]], [[(long)4]]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (7,17): error CS0411: The type arguments for method 'Program.F1<T>(T[], T[])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x = F1([(long)1], [(int?)2]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1").WithArguments("Program.F1<T>(T[], T[])").WithLocation(7, 17),
                // (8,17): error CS0411: The type arguments for method 'Program.F2<T>(T[][], T[][])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var y = F2([[(int?)3]], [[(long)4]]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F2").WithArguments("Program.F2<T>(T[][], T[][])").WithLocation(8, 17));
        }

        [Fact]
        public void TypeInference_18()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static List<T[]> AsListOfArray<T>(List<T[]> arg) => arg;
                    static void Main()
                    {
                        var x = AsListOfArray([[4, 5], []]);
                        x.Report(true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Collections.Generic.List<System.Int32[]>) [[4, 5], []], ");
        }

        [Fact]
        public void TypeInference_19()
        {
            string source = """
                class Program
                {
                    static T[] F<T>(T[][] x) => x[1];
                    static void Main()
                    {
                        var y = F([new byte[0], [1, 2, 3]]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,17): error CS0411: The type arguments for method 'Program.F<T>(T[][])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var y = F([new byte[0], [1, 2, 3]]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T[][])").WithLocation(6, 17));
        }

        [Fact]
        public void TypeInference_20()
        {
            string source = """
                class Program
                {
                    static T[] F<T>(in T[] x, T[] y) => x;
                    static void Main()
                    {
                        var x = F([1], [2]);
                        x.Report(true);
                        var y = F([3], [(object)4]);
                        y.Report(true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [1], (System.Object[]) [3], ");
        }

        [Fact]
        public void TypeInference_21()
        {
            string source = """
                class Program
                {
                    static T[] F<T>(in T[] x, T[] y) => x;
                    static void Main()
                    {
                        var y = F(in [3], [(object)4]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,22): error CS8156: An expression cannot be used in this context because it may not be passed or returned by reference
                //         var y = F(in [3], [(object)4]);
                Diagnostic(ErrorCode.ERR_RefReturnLvalueExpected, "[3]").WithLocation(6, 22));
        }

        [Fact]
        public void TypeInference_22()
        {
            string source = """
                class Program
                {
                    static T[] F1<T>(T[] x, T[] y) => y;
                    static T[] F2<T>(T[][] x, T[][] y) => y[0];
                    static void Main()
                    {
                        var x = F1([], [default, 2]);
                        x.Report(true);
                        var y = F2([[null]], [[default, (int?)4]]);
                        y.Report(true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [0, 2], (System.Nullable<System.Int32>[]) [null, 4], ");
        }

        [Fact]
        public void TypeInference_23()
        {
            string source = """
                class Program
                {
                    static T[] F1<T>(T[] x, T[] y) => y;
                    static T[] F2<T>(T[][] x, T[][] y) => y[0];
                    static void Main()
                    {
                        var x = F1([], [default]);
                        var y = F2([[null]], [[default]]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (7,17): error CS0411: The type arguments for method 'Program.F1<T>(T[], T[])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x = F1([], [default]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1").WithArguments("Program.F1<T>(T[], T[])").WithLocation(7, 17),
                // (8,17): error CS0411: The type arguments for method 'Program.F2<T>(T[][], T[][])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var y = F2([[null]], [[default]]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F2").WithArguments("Program.F2<T>(T[][], T[][])").WithLocation(8, 17));
        }

        [Fact]
        public void TypeInference_24()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static ReadOnlySpan<T> F1<T>(Span<T> x, ReadOnlySpan<T> y) => y;
                    static List<T> F2<T>(Span<T[]> x, ReadOnlySpan<List<T>> y) => y[0];
                    static void Main()
                    {
                        var x = F1([], [default, 2]);
                        x.Report();
                        var y = F2([[null]], [[default, (int?)4]]);
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net70,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[0, 2], [null, 4], "));
        }

        [Fact]
        public void TypeInference_25()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static ReadOnlySpan<T> F1<T>(Span<T> x, ReadOnlySpan<T> y) => y;
                    static List<T> F2<T>(Span<T[]> x, ReadOnlySpan<List<T>> y) => y[0];
                    static void Main()
                    {
                        var x = F1([], [default]);
                        var y = F2([[null]], [[default]]);
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net70);
            comp.VerifyEmitDiagnostics(
                // (9,17): error CS0411: The type arguments for method 'Program.F1<T>(Span<T>, ReadOnlySpan<T>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x = F1([], [default]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1").WithArguments("Program.F1<T>(System.Span<T>, System.ReadOnlySpan<T>)").WithLocation(9, 17),
                // (10,17): error CS0411: The type arguments for method 'Program.F2<T>(Span<T[]>, ReadOnlySpan<List<T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var y = F2([[null]], [[default]]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F2").WithArguments("Program.F2<T>(System.Span<T[]>, System.ReadOnlySpan<System.Collections.Generic.List<T>>)").WithLocation(10, 17));
        }

        [Fact]
        public void TypeInference_26()
        {
            string source = """
                class Program
                {
                    static void F<T>(T x) { }
                    static void Main()
                    {
                        F([]);
                        F([null, default, 0]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,9): error CS0411: The type arguments for method 'Program.F<T>(T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T)").WithLocation(6, 9),
                // (7,9): error CS0411: The type arguments for method 'Program.F<T>(T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([null, default, 0]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T)").WithLocation(7, 9));
        }

        [Fact]
        public void TypeInference_27()
        {
            string source = """
                class Program
                {
                    static void F<T>(T[,] x) { }
                    static void Main()
                    {
                        F([]);
                        F([null, default, 0]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,9): error CS0411: The type arguments for method 'Program.F<T>(T[*,*])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T[*,*])").WithLocation(6, 9),
                // (7,11): error CS1503: Argument 1: cannot convert from 'collection expressions' to 'int[*,*]'
                //         F([null, default, 0]);
                Diagnostic(ErrorCode.ERR_BadArgType, "[null, default, 0]").WithArguments("1", "collection expressions", "int[*,*]").WithLocation(7, 11));
        }

        [Fact]
        public void TypeInference_28()
        {
            string source = """
                class Program
                {
                    static void F<T>(string x, T[] y) { }
                    static void Main()
                    {
                        F([], ['B']);
                        F([default], ['B']);
                        F(['A'], ['B']);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,11): error CS1729: 'string' does not contain a constructor that takes 0 arguments
                //         F([], ['B']);
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "[]").WithArguments("string", "0").WithLocation(6, 11),
                // (7,11): error CS1729: 'string' does not contain a constructor that takes 0 arguments
                //         F([default], ['B']);
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "[default]").WithArguments("string", "0").WithLocation(7, 11),
                // (7,12): error CS1061: 'string' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'string' could be found (are you missing a using directive or an assembly reference?)
                //         F([default], ['B']);
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "default").WithArguments("string", "Add").WithLocation(7, 12),
                // (8,11): error CS1729: 'string' does not contain a constructor that takes 0 arguments
                //         F(['A'], ['B']);
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "['A']").WithArguments("string", "0").WithLocation(8, 11),
                // (8,12): error CS1061: 'string' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'string' could be found (are you missing a using directive or an assembly reference?)
                //         F(['A'], ['B']);
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "'A'").WithArguments("string", "Add").WithLocation(8, 12));
        }

        [Fact]
        public void TypeInference_29()
        {
            string source = """
                delegate void D();
                enum E { }
                class Program
                {
                    static void F1<T>(dynamic x, T[] y) { }
                    static void F2<T>(D x, T[] y) { }
                    static void F3<T>(E x, T[] y) { }
                    static void Main()
                    {
                        F1([1], [2]);
                        F2([3], [4]);
                        F3([5], [6]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (10,12): error CS1503: Argument 1: cannot convert from 'collection expressions' to 'dynamic'
                //         F1([1], [2]);
                Diagnostic(ErrorCode.ERR_BadArgType, "[1]").WithArguments("1", "collection expressions", "dynamic").WithLocation(10, 12),
                // (11,12): error CS1503: Argument 1: cannot convert from 'collection expressions' to 'D'
                //         F2([3], [4]);
                Diagnostic(ErrorCode.ERR_BadArgType, "[3]").WithArguments("1", "collection expressions", "D").WithLocation(11, 12),
                // (12,12): error CS1503: Argument 1: cannot convert from 'collection expressions' to 'E'
                //         F3([5], [6]);
                Diagnostic(ErrorCode.ERR_BadArgType, "[5]").WithArguments("1", "collection expressions", "E").WithLocation(12, 12));
        }

        [Fact]
        public void TypeInference_30()
        {
            string source = """
                delegate void D();
                enum E { }
                class Program
                {
                    static void F1<T>(dynamic[] x, T[] y) { }
                    static void F2<T>(D[] x, T[] y) { }
                    static void F3<T>(E[] x, T[] y) { }
                    static void Main()
                    {
                        F1([1], [2]);
                        F2([null], [4]);
                        F3([default], [6]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();
        }

        [Fact]
        public void TypeInference_31()
        {
            string source = """
                class Program
                {
                    static void F<T>(T[] x) { }
                    static void Main()
                    {
                        F([null]);
                        F([Unknown]);
                        F([Main()]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,9): error CS0411: The type arguments for method 'Program.F<T>(T[])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([null]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T[])").WithLocation(6, 9),
                // (7,12): error CS0103: The name 'Unknown' does not exist in the current context
                //         F([Unknown]);
                Diagnostic(ErrorCode.ERR_NameNotInContext, "Unknown").WithArguments("Unknown").WithLocation(7, 12),
                // (8,9): error CS0411: The type arguments for method 'Program.F<T>(T[])' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         F([Main()]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(T[])").WithLocation(8, 9));
        }

        [Fact]
        public void TypeInference_32()
        {
            string source = """
                delegate void D();
                class Program
                {
                    static T[] F<T>(T[] x) => x;
                    static void Main()
                    {
                        var x = F([null, Main]);
                        x.Report(includeType: true);
                        var y = F([Main, (D)Main]);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Action[]) [null, System.Action], (D[]) [D, D], ");
        }

        [Fact]
        public void TypeInference_33()
        {
            string source = """
                delegate byte D();
                class Program
                {
                    static T[] F<T>(T[] x) => x;
                    static void Main()
                    {
                        var x = F([null, () => 1]);
                        x.Report(includeType: true);
                        var y = F([() => 2, (D)(() => 3)]);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Func<System.Int32>[]) [null, System.Func`1[System.Int32]], (D[]) [D, D], ");
        }

        [Fact]
        public void TypeInference_34()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static List<Func<T>> F1<T>(List<Func<T>> x) => x;
                    static string F2() => null;
                    static void Main()
                    {
                        var x = F1([F2]);
                        x.Report();
                        var y = F1([null, () => 1]);
                        y.Report();
                        var z = F1([F2, () => default]);
                        z.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensions },
                expectedOutput: "[System.Func`1[System.String]], [null, System.Func`1[System.Int32]], [System.Func`1[System.String], System.Func`1[System.String]], ");
        }

        [Fact]
        public void TypeInference_35()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static List<Action<T>> F1<T>(List<Action<T>> x) => x;
                    static void F2(string s) { }
                    static void Main()
                    {
                        var x = F1([F2, (string s) => { }]);
                        x.Report();
                        var y = F1([null, (int a) => { }]);
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensions },
                expectedOutput: "[System.Action`1[System.String], System.Action`1[System.String]], [null, System.Action`1[System.Int32]], ");
        }

        [Fact]
        public void TypeInference_36()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static List<Func<T>> F1<T>(List<Func<T>> x) => x;
                    static string F2() => null;
                    static void Main()
                    {
                        var x = F1([() => default]);
                        var y = F1([() => 2, F2]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (9,17): error CS0411: The type arguments for method 'Program.F1<T>(List<Func<T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x = F1([() => default]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1").WithArguments("Program.F1<T>(System.Collections.Generic.List<System.Func<T>>)").WithLocation(9, 17),
                // (10,17): error CS0411: The type arguments for method 'Program.F1<T>(List<Func<T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var y = F1([null, () => 1]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F1").WithArguments("Program.F1<T>(System.Collections.Generic.List<System.Func<T>>)").WithLocation(10, 17));
        }

        [Fact]
        public void TypeInference_37()
        {
            string source = """
                class Program
                {
                    static (T, U)[] F<T, U>((T, U)[] x) => x;
                    static void Main()
                    {
                        var x = F([(1, "2")]);
                        x.Report(includeType: true);
                        var y = F([default, (3, (byte)4)]);
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensions },
                expectedOutput: "(System.ValueTuple<System.Int32, System.String>[]) [(1, 2)], (System.ValueTuple<System.Int32, System.Byte>[]) [(0, 0), (3, 4)], ");
        }

        [Fact]
        public void TypeInference_38()
        {
            string source = """
                class Program
                {
                    static T[] F1<T>(T[] x, T y) => x;
                    static T[] F2<T>(T[] x, ref T y) => x;
                    static T[] F3<T>(T[] x, in T y) => x;
                    static T[] F4<T>(T[] x, out T y) { y = default; return x; }
                    static void Main()
                    {
                        object y = null;
                        var x1 = F1([1], y);
                        var x2 = F2([2], ref y);
                        var x3A = F3([3], y);
                        var x3B = F3([3], in y);
                        var x4 = F4([4], out y);
                        x1.Report(true);
                        x2.Report(true);
                        x3A.Report(true);
                        x3B.Report(true);
                        x4.Report(true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Object[]) [1], (System.Object[]) [2], (System.Object[]) [3], (System.Object[]) [3], (System.Object[]) [4], ");
        }

        [Fact]
        public void TypeInference_39A()
        {
            string source = """
                class Program
                {
                    static T[] F1<T>(T[] x, T y) => x;
                    static T[] F3<T>(T[] x, in T y) => x;
                    static void Main()
                    {
                        byte y = 0;
                        var x1 = F1([1], y);
                        var x3A = F3([3], y);
                        x1.Report(true);
                        x3A.Report(true);
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "(System.Int32[]) [1], (System.Int32[]) [3], ");
        }

        [Fact]
        public void TypeInference_39B()
        {
            string source = """
                class Program
                {
                    static T[] F2<T>(T[] x, ref T y) => x;
                    static T[] F3<T>(T[] x, in T y) => x;
                    static T[] F4<T>(T[] x, out T y) { y = default; return x; }
                    static void Main()
                    {
                        byte y = 0;
                        var x2 = F2([2], ref y);
                        var x3B = F3([3], in y);
                        var x4 = F4([4], out y);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (9,18): error CS0411: The type arguments for method 'Program.F2<T>(T[], ref T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x2 = F2([2], ref y);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F2").WithArguments("Program.F2<T>(T[], ref T)").WithLocation(9, 18),
                // (10,19): error CS0411: The type arguments for method 'Program.F3<T>(T[], in T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x3B = F3([3], in y);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F3").WithArguments("Program.F3<T>(T[], in T)").WithLocation(10, 19),
                // (11,18): error CS0411: The type arguments for method 'Program.F4<T>(T[], out T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x4 = F4([4], out y);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F4").WithArguments("Program.F4<T>(T[], out T)").WithLocation(11, 18));
        }

        [Fact]
        public void TypeInference_40()
        {
            string source = """
                using System;
                class Program
                {
                    static Func<T[]> F<T>(Func<T[]> arg) => arg;
                    static void Main(string[] args)
                    {
                        var x = F(() => [1, 2, 3]);
                        x.Report(includeType: true);
                        var y = F(() => { if (args.Length == 0) return []; return [1, 2, 3]; });
                        y.Report(includeType: true);
                    }
                }
                """;
            var comp = CreateCompilation(new[] { source, s_collectionExtensions });
            comp.VerifyEmitDiagnostics(
                // 0.cs(7,17): error CS0411: The type arguments for method 'Program.F<T>(Func<T[]>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var x = F(() => [1, 2, 3]);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(System.Func<T[]>)").WithLocation(7, 17),
                // 0.cs(9,17): error CS0411: The type arguments for method 'Program.F<T>(Func<T[]>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         var y = F(() => { if (args.Length == 0) return []; return [1, 2, 3]; });
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "F").WithArguments("Program.F<T>(System.Func<T[]>)").WithLocation(9, 17));
        }

        [Fact]
        public void MemberAccess_01()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        [].GetHashCode();
                        []?.GetHashCode();
                        [][0].GetHashCode();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,9): error CS9176: There is no target type for the collection expression.
                //         [].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(5, 9),
                // (6,9): error CS9176: There is no target type for the collection expression.
                //         []?.GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(6, 9),
                // (7,9): error CS9176: There is no target type for the collection expression.
                //         [][0].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 9));
        }

        [Fact]
        public void MemberAccess_02()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        [1].GetHashCode();
                        [2]?.GetHashCode();
                        [3][0].GetHashCode();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,9): error CS9176: There is no target type for the collection expression.
                //         [1].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[1]").WithLocation(5, 9),
                // (6,9): error CS9176: There is no target type for the collection expression.
                //         [2]?.GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[2]").WithLocation(6, 9),
                // (7,9): error CS9176: There is no target type for the collection expression.
                //         [3][0].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[3]").WithLocation(7, 9));
        }

        [Fact]
        public void MemberAccess_03()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        _ = [].GetHashCode();
                        _ = []?.GetHashCode();
                        _ = [][0].GetHashCode();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,13): error CS9176: There is no target type for the collection expression.
                //         _ = [].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(5, 13),
                // (6,13): error CS9176: There is no target type for the collection expression.
                //         _ = []?.GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(6, 13),
                // (7,13): error CS9176: There is no target type for the collection expression.
                //         _ = [][0].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 13));
        }

        [Fact]
        public void MemberAccess_04()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        _ = [1].GetHashCode();
                        _ = [2]?.GetHashCode();
                        _ = [3][0].GetHashCode();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,13): error CS9176: There is no target type for the collection expression.
                //         _ = [1].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[1]").WithLocation(5, 13),
                // (6,13): error CS9176: There is no target type for the collection expression.
                //         _ = [2]?.GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[2]").WithLocation(6, 13),
                // (7,13): error CS9176: There is no target type for the collection expression.
                //         _ = [3][0].GetHashCode();
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[3]").WithLocation(7, 13));
        }

        [Fact]
        public void ListBase()
        {
            string sourceA = """
                using System.Collections;
                namespace System
                {
                    public class Object { }
                    public abstract class ValueType { }
                    public class String { }
                    public class Type { }
                    public struct Void { }
                    public struct Boolean { }
                    public struct Int32 { }
                }
                namespace System.Collections
                {
                    public interface IEnumerable { }
                }
                namespace System.Collections.Generic
                {
                    public class ListBase<T> : IEnumerable
                    {
                        public void Add(string s) { }
                    }
                    public class List<T> : ListBase<T>
                    {
                        public void Add(T t) { }
                    }
                }
                """;
            string sourceB = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        ListBase<int> x = [];
                        ListBase<int> y = [1, 2];
                        ListBase<string> z = ["a", "b"];
                    }
                }
                """;
            var comp = CreateEmptyCompilation(new[] { sourceA, sourceB }, parseOptions: TestOptions.RegularPreview.WithNoRefSafetyRulesAttribute());
            comp.VerifyEmitDiagnostics(
                // warning CS8021: No value for RuntimeMetadataVersion found. No assembly containing System.Object was found nor was a value for RuntimeMetadataVersion specified through options.
                Diagnostic(ErrorCode.WRN_NoRuntimeMetadataVersion).WithLocation(1, 1),
                // 1.cs(7,28): error CS1950: The best overloaded Add method 'ListBase<int>.Add(string)' for the collection initializer has some invalid arguments
                //         ListBase<int> y = [1, 2];
                Diagnostic(ErrorCode.ERR_BadArgTypesForCollectionAdd, "1").WithArguments("System.Collections.Generic.ListBase<int>.Add(string)").WithLocation(7, 28),
                // 1.cs(7,28): error CS1503: Argument 1: cannot convert from 'int' to 'string'
                //         ListBase<int> y = [1, 2];
                Diagnostic(ErrorCode.ERR_BadArgType, "1").WithArguments("1", "int", "string").WithLocation(7, 28),
                // 1.cs(7,31): error CS1950: The best overloaded Add method 'ListBase<int>.Add(string)' for the collection initializer has some invalid arguments
                //         ListBase<int> y = [1, 2];
                Diagnostic(ErrorCode.ERR_BadArgTypesForCollectionAdd, "2").WithArguments("System.Collections.Generic.ListBase<int>.Add(string)").WithLocation(7, 31),
                // 1.cs(7,31): error CS1503: Argument 1: cannot convert from 'int' to 'string'
                //         ListBase<int> y = [1, 2];
                Diagnostic(ErrorCode.ERR_BadArgType, "2").WithArguments("1", "int", "string").WithLocation(7, 31));
        }

        [Fact]
        public void ListInterfaces_01()
        {
            string sourceA = """
                namespace System
                {
                    public class Object { }
                    public abstract class ValueType { }
                    public class String { }
                    public class Type { }
                    public struct Void { }
                    public struct Boolean { }
                    public struct Int32 { }
                }
                namespace System.Collections
                {
                    public interface IEnumerable { }
                }
                namespace System.Collections.Generic
                {
                    public interface IA { }
                    public interface IB<T> { }
                    public interface IC<T> { }
                    public interface ID<T1, T2> { }
                    public class List<T> : IEnumerable, IA, IB<T>, IC<object>, ID<T, object>
                    {
                        public void Add(T t) { }
                    }
                }
                """;
            string sourceB = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        List<int> l = [1];
                        IA a = [2];
                        IB<object> b = [3];
                        IC<object> c = [4];
                        ID<object, object> d = [5];
                    }
                }
                """;
            var comp = CreateEmptyCompilation(new[] { sourceA, sourceB }, parseOptions: TestOptions.RegularPreview.WithNoRefSafetyRulesAttribute());
            comp.VerifyEmitDiagnostics(
                // warning CS8021: No value for RuntimeMetadataVersion found. No assembly containing System.Object was found nor was a value for RuntimeMetadataVersion specified through options.
                Diagnostic(ErrorCode.WRN_NoRuntimeMetadataVersion).WithLocation(1, 1),
                // 1.cs(7,16): error CS9174: Cannot initialize type 'IA' with a collection expression because the type is not constructible.
                //         IA a = [2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("System.Collections.Generic.IA").WithLocation(7, 16),
                // 1.cs(9,24): error CS9174: Cannot initialize type 'IC<object>' with a collection expression because the type is not constructible.
                //         IC<object> c = [4];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[4]").WithArguments("System.Collections.Generic.IC<object>").WithLocation(9, 24),
                // 1.cs(10,32): error CS9174: Cannot initialize type 'ID<object, object>' with a collection expression because the type is not constructible.
                //         ID<object, object> d = [5];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[5]").WithArguments("System.Collections.Generic.ID<object, object>").WithLocation(10, 32));
        }

        [Fact]
        public void ListInterfaces_02()
        {
            string sourceA = """
                namespace System
                {
                    public class Object { }
                    public abstract class ValueType { }
                    public class String { }
                    public class Type { }
                    public struct Void { }
                    public struct Boolean { }
                    public struct Int32 { }
                    public interface IEquatable<T>
                    {
                        bool Equals(T other);
                    }
                }
                namespace System.Collections
                {
                    public interface IEnumerable { }
                }
                namespace System.Collections.Generic
                {
                    public class List<T> : IEnumerable, IEquatable<List<T>>
                    {
                        public bool Equals(List<T> other) => false;
                        public void Add(T t) { }
                    }
                }
                """;
            string sourceB = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        List<int> l = [1];
                        IEquatable<int> e = [2];
                    }
                }
                """;
            var comp = CreateEmptyCompilation(new[] { sourceA, sourceB }, parseOptions: TestOptions.RegularPreview.WithNoRefSafetyRulesAttribute());
            comp.VerifyEmitDiagnostics(
                // warning CS8021: No value for RuntimeMetadataVersion found. No assembly containing System.Object was found nor was a value for RuntimeMetadataVersion specified through options.
                Diagnostic(ErrorCode.WRN_NoRuntimeMetadataVersion).WithLocation(1, 1),
                // 1.cs(8,29): error CS9174: Cannot initialize type 'IEquatable<int>' with a collection expression because the type is not constructible.
                //         IEquatable<int> e = [2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("System.IEquatable<int>").WithLocation(8, 29));
        }

        [Fact]
        public void ListInterfaces_NoInterfaces()
        {
            string sourceA = """
                namespace System
                {
                    public class Object { }
                    public abstract class ValueType { }
                    public class String { }
                    public class Type { }
                    public struct Void { }
                    public struct Boolean { }
                    public struct Int32 { }
                }
                namespace System.Collections.Generic
                {
                    public interface IEnumerable<T> { }
                    public class List<T>
                    {
                        public void Add(T t) { }
                    }
                }
                """;
            string sourceB = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        List<int> l = [1];
                        IEnumerable<int> e = [2];
                    }
                }
                """;
            var comp = CreateEmptyCompilation(new[] { sourceA, sourceB }, parseOptions: TestOptions.RegularPreview.WithNoRefSafetyRulesAttribute());
            comp.VerifyEmitDiagnostics(
                // warning CS8021: No value for RuntimeMetadataVersion found. No assembly containing System.Object was found nor was a value for RuntimeMetadataVersion specified through options.
                Diagnostic(ErrorCode.WRN_NoRuntimeMetadataVersion).WithLocation(1, 1),
                // 1.cs(7,23): error CS9174: Cannot initialize type 'List<int>' with a collection expression because the type is not constructible.
                //         List<int> l = [1];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1]").WithArguments("System.Collections.Generic.List<int>").WithLocation(7, 23),
                // 1.cs(8,30): error CS9174: Cannot initialize type 'IEnumerable<int>' with a collection expression because the type is not constructible.
                //         IEnumerable<int> e = [2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("System.Collections.Generic.IEnumerable<int>").WithLocation(8, 30));
        }

        [Fact]
        public void ListInterfaces_MissingList()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        IEnumerable<int> a = [];
                        ICollection<int> b = [2];
                        IList<int> c = [];
                        IReadOnlyCollection<int> d = [3];
                        IReadOnlyList<int> e = [];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.MakeTypeMissing(WellKnownType.System_Collections_Generic_List_T);
            comp.VerifyEmitDiagnostics(
                // (6,30): error CS9174: Cannot initialize type 'IEnumerable<int>' with a collection expression because the type is not constructible.
                //         IEnumerable<int> a = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("System.Collections.Generic.IEnumerable<int>").WithLocation(6, 30),
                // (7,30): error CS9174: Cannot initialize type 'ICollection<int>' with a collection expression because the type is not constructible.
                //         ICollection<int> b = [2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("System.Collections.Generic.ICollection<int>").WithLocation(7, 30),
                // (8,24): error CS9174: Cannot initialize type 'IList<int>' with a collection expression because the type is not constructible.
                //         IList<int> c = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("System.Collections.Generic.IList<int>").WithLocation(8, 24),
                // (9,38): error CS9174: Cannot initialize type 'IReadOnlyCollection<int>' with a collection expression because the type is not constructible.
                //         IReadOnlyCollection<int> d = [3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[3]").WithArguments("System.Collections.Generic.IReadOnlyCollection<int>").WithLocation(9, 38),
                // (10,32): error CS9174: Cannot initialize type 'IReadOnlyList<int>' with a collection expression because the type is not constructible.
                //         IReadOnlyList<int> e = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("System.Collections.Generic.IReadOnlyList<int>").WithLocation(10, 32));
        }

        [Fact]
        public void Array_01()
        {
            string source = """
                class Program
                {
                    static int[] Create1() => [];
                    static object[] Create2() => [1, 2];
                    static int[] Create3() => [3, 4, 5];
                    static long?[] Create4() => [null, 7];
                    static void Main()
                    {
                        Create1().Report();
                        Create2().Report();
                        Create3().Report();
                        Create4().Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [1, 2], [3, 4, 5], [null, 7], ");
            verifier.VerifyIL("Program.Create1", """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  call       "int[] System.Array.Empty<int>()"
                  IL_0005:  ret
                }
                """);
            verifier.VerifyIL("Program.Create2", """
                {
                  // Code size       25 (0x19)
                  .maxstack  4
                  IL_0000:  ldc.i4.2
                  IL_0001:  newarr     "object"
                  IL_0006:  dup
                  IL_0007:  ldc.i4.0
                  IL_0008:  ldc.i4.1
                  IL_0009:  box        "int"
                  IL_000e:  stelem.ref
                  IL_000f:  dup
                  IL_0010:  ldc.i4.1
                  IL_0011:  ldc.i4.2
                  IL_0012:  box        "int"
                  IL_0017:  stelem.ref
                  IL_0018:  ret
                }
                """);
            verifier.VerifyIL("Program.Create3", """
                {
                  // Code size       18 (0x12)
                  .maxstack  3
                  IL_0000:  ldc.i4.3
                  IL_0001:  newarr     "int"
                  IL_0006:  dup
                  IL_0007:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12 <PrivateImplementationDetails>.CE99AE045C8B2A2A8A58FD1A2120956E74E90322EEF45F7DFE1CA73EEFE655D4"
                  IL_000c:  call       "void System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(System.Array, System.RuntimeFieldHandle)"
                  IL_0011:  ret
                }
                """);
            verifier.VerifyIL("Program.Create4", """
                {
                  // Code size       21 (0x15)
                  .maxstack  4
                  IL_0000:  ldc.i4.2
                  IL_0001:  newarr     "long?"
                  IL_0006:  dup
                  IL_0007:  ldc.i4.1
                  IL_0008:  ldc.i4.7
                  IL_0009:  conv.i8
                  IL_000a:  newobj     "long?..ctor(long)"
                  IL_000f:  stelem     "long?"
                  IL_0014:  ret
                }
                """);
        }

        [Fact]
        public void Array_02()
        {
            string source = """
                using System;
                class Program
                {
                    static int[][] Create1() => [];
                    static object[][] Create2() => [[]];
                    static object[][] Create3() => [[1], [2, 3]];
                    static void Main()
                    {
                        Report(Create1());
                        Report(Create2());
                        Report(Create3());
                    }
                    static void Report<T>(T[][] a)
                    {
                        Console.Write("Length={0}, ", a.Length);
                        foreach (var x in a)
                        {
                            Console.Write("Length={0}, ", x.Length);
                            foreach (var y in x)
                                Console.Write("{0}, ", y);
                        }
                        Console.WriteLine();
                    }
                }
                """;
            var verifier = CompileAndVerify(source, expectedOutput: """
                Length=0, 
                Length=1, Length=0, 
                Length=2, Length=1, 1, Length=2, 2, 3, 
                """);
            verifier.VerifyIL("Program.Create1", """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  call       "int[][] System.Array.Empty<int[]>()"
                  IL_0005:  ret
                }
                """);
            verifier.VerifyIL("Program.Create2", """
                {
                  // Code size       15 (0xf)
                  .maxstack  4
                  IL_0000:  ldc.i4.1
                  IL_0001:  newarr     "object[]"
                  IL_0006:  dup
                  IL_0007:  ldc.i4.0
                  IL_0008:  call       "object[] System.Array.Empty<object>()"
                  IL_000d:  stelem.ref
                  IL_000e:  ret
                }
                """);
            verifier.VerifyIL("Program.Create3", """
                {
                  // Code size       52 (0x34)
                  .maxstack  7
                  IL_0000:  ldc.i4.2
                  IL_0001:  newarr     "object[]"
                  IL_0006:  dup
                  IL_0007:  ldc.i4.0
                  IL_0008:  ldc.i4.1
                  IL_0009:  newarr     "object"
                  IL_000e:  dup
                  IL_000f:  ldc.i4.0
                  IL_0010:  ldc.i4.1
                  IL_0011:  box        "int"
                  IL_0016:  stelem.ref
                  IL_0017:  stelem.ref
                  IL_0018:  dup
                  IL_0019:  ldc.i4.1
                  IL_001a:  ldc.i4.2
                  IL_001b:  newarr     "object"
                  IL_0020:  dup
                  IL_0021:  ldc.i4.0
                  IL_0022:  ldc.i4.2
                  IL_0023:  box        "int"
                  IL_0028:  stelem.ref
                  IL_0029:  dup
                  IL_002a:  ldc.i4.1
                  IL_002b:  ldc.i4.3
                  IL_002c:  box        "int"
                  IL_0031:  stelem.ref
                  IL_0032:  stelem.ref
                  IL_0033:  ret
                }
                """);
        }

        [Fact]
        public void Array_03()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object o;
                        o = (int[])[];
                        o.Report();
                        o = (long?[])[null, 2];
                        o.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [null, 2], ");
        }

        [Fact]
        public void Array_04()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        object[,] x = [];
                        int[,] y = [null, 2];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,23): error CS9174: Cannot initialize type 'object[*,*]' with a collection expression because the type is not constructible.
                //         object[,] x = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("object[*,*]").WithLocation(5, 23),
                // (6,20): error CS9174: Cannot initialize type 'int[*,*]' with a collection expression because the type is not constructible.
                //         int[,] y = [null, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[null, 2]").WithArguments("int[*,*]").WithLocation(6, 20));
        }

        [Fact]
        public void Array_05()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        int[,] z = [[1, 2], [3, 4]];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,20): error CS9174: Cannot initialize type 'int[*,*]' with a collection expression because the type is not constructible.
                //         int[,] z = [[1, 2], [3, 4]];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[[1, 2], [3, 4]]").WithArguments("int[*,*]").WithLocation(5, 20));
        }

        [Theory]
        [CombinatorialData]
        public void Span_01(bool useReadOnlySpan)
        {
            string spanType = useReadOnlySpan ? "ReadOnlySpan" : "Span";
            string source = $$"""
                using System;
                class Program
                {
                    static void Create1() { {{spanType}}<int> s = []; s.Report(); }
                    static void Create2() { {{spanType}}<object> s = [1, 2]; s.Report(); }
                    static void Create3() { {{spanType}}<int> s = [3, 4, 5]; s.Report(); }
                    static void Create4() { {{spanType}}<long?> s = [null, 7]; s.Report(); }
                    static void Main()
                    {
                        Create1();
                        Create2();
                        Create3();
                        Create4();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net70,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[], [1, 2], [3, 4, 5], [null, 7], "));
            verifier.VerifyIL("Program.Create1", $$"""
                {
                  // Code size       20 (0x14)
                  .maxstack  2
                  .locals init (System.{{spanType}}<int> V_0) //s
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  call       "int[] System.Array.Empty<int>()"
                  IL_0007:  call       "System.{{spanType}}<int>..ctor(int[])"
                  IL_000c:  ldloca.s   V_0
                  IL_000e:  call       "void CollectionExtensions.Report<int>(in System.{{spanType}}<int>)"
                  IL_0013:  ret
                }
                """);
            verifier.VerifyIL("Program.Create2", $$"""
                {
                  // Code size       39 (0x27)
                  .maxstack  5
                  .locals init (System.{{spanType}}<object> V_0) //s
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  ldc.i4.2
                  IL_0003:  newarr     "object"
                  IL_0008:  dup
                  IL_0009:  ldc.i4.0
                  IL_000a:  ldc.i4.1
                  IL_000b:  box        "int"
                  IL_0010:  stelem.ref
                  IL_0011:  dup
                  IL_0012:  ldc.i4.1
                  IL_0013:  ldc.i4.2
                  IL_0014:  box        "int"
                  IL_0019:  stelem.ref
                  IL_001a:  call       "System.{{spanType}}<object>..ctor(object[])"
                  IL_001f:  ldloca.s   V_0
                  IL_0021:  call       "void CollectionExtensions.Report<object>(in System.{{spanType}}<object>)"
                  IL_0026:  ret
                }
                """);
            if (useReadOnlySpan)
            {
                verifier.VerifyIL("Program.Create3", """
                    {
                      // Code size       19 (0x13)
                      .maxstack  1
                      .locals init (System.ReadOnlySpan<int> V_0) //s
                      IL_0000:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12_Align=4 <PrivateImplementationDetails>.CE99AE045C8B2A2A8A58FD1A2120956E74E90322EEF45F7DFE1CA73EEFE655D44"
                      IL_0005:  call       "System.ReadOnlySpan<int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<int>(System.RuntimeFieldHandle)"
                      IL_000a:  stloc.0
                      IL_000b:  ldloca.s   V_0
                      IL_000d:  call       "void CollectionExtensions.Report<int>(in System.ReadOnlySpan<int>)"
                      IL_0012:  ret
                    }
                    """);
            }
            else
            {
                verifier.VerifyIL("Program.Create3", """
                    {
                      // Code size       32 (0x20)
                      .maxstack  4
                      .locals init (System.Span<int> V_0) //s
                      IL_0000:  ldloca.s   V_0
                      IL_0002:  ldc.i4.3
                      IL_0003:  newarr     "int"
                      IL_0008:  dup
                      IL_0009:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12 <PrivateImplementationDetails>.CE99AE045C8B2A2A8A58FD1A2120956E74E90322EEF45F7DFE1CA73EEFE655D4"
                      IL_000e:  call       "void System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(System.Array, System.RuntimeFieldHandle)"
                      IL_0013:  call       "System.Span<int>..ctor(int[])"
                      IL_0018:  ldloca.s   V_0
                      IL_001a:  call       "void CollectionExtensions.Report<int>(in System.Span<int>)"
                      IL_001f:  ret
                    }
                    """);
            }
            verifier.VerifyIL("Program.Create4", $$"""
                {
                  // Code size       35 (0x23)
                  .maxstack  5
                  .locals init (System.{{spanType}}<long?> V_0) //s
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  ldc.i4.2
                  IL_0003:  newarr     "long?"
                  IL_0008:  dup
                  IL_0009:  ldc.i4.1
                  IL_000a:  ldc.i4.7
                  IL_000b:  conv.i8
                  IL_000c:  newobj     "long?..ctor(long)"
                  IL_0011:  stelem     "long?"
                  IL_0016:  call       "System.{{spanType}}<long?>..ctor(long?[])"
                  IL_001b:  ldloca.s   V_0
                  IL_001d:  call       "void CollectionExtensions.Report<long?>(in System.{{spanType}}<long?>)"
                  IL_0022:  ret
                }
                """);
        }

        [Theory]
        [CombinatorialData]
        public void Span_02(bool useReadOnlySpan)
        {
            string spanType = useReadOnlySpan ? "ReadOnlySpan" : "Span";
            string source = $$"""
                using System;
                class Program
                {
                    static void Main()
                    {
                        {{spanType}}<string> x = [];
                        {{spanType}}<int> y = [1, 2, 3];
                        x.Report();
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net70,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [Theory]
        [CombinatorialData]
        public void Span_03(bool useReadOnlySpan)
        {
            string spanType = useReadOnlySpan ? "ReadOnlySpan" : "Span";
            string source = $$"""
                using System;
                class Program
                {
                    static void Main()
                    {
                        var x = ({{spanType}}<string>)[];
                        var y = ({{spanType}}<int>)[1, 2, 3];
                        x.Report();
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net70,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [Theory]
        [CombinatorialData]
        public void Span_04(bool useReadOnlySpan)
        {
            string spanType = useReadOnlySpan ? "ReadOnlySpan" : "Span";
            string source = $$"""
                using System;
                class Program
                {
                    static ref readonly {{spanType}}<int> F1()
                    {
                        return ref F2<int>([]);
                    }
                    static ref readonly {{spanType}}<T> F2<T>(in {{spanType}}<T> s)
                    {
                        return ref s;
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net70);
            comp.VerifyEmitDiagnostics(
                // (6,20): error CS8347: Cannot use a result of 'Program.F2<int>(in Span<int>)' in this context because it may expose variables referenced by parameter 's' outside of their declaration scope
                //         return ref F2<int>([]);
                Diagnostic(ErrorCode.ERR_EscapeCall, "F2<int>([])").WithArguments($"Program.F2<int>(in System.{spanType}<int>)", "s").WithLocation(6, 20),
                // (6,28): error CS8156: An expression cannot be used in this context because it may not be passed or returned by reference
                //         return ref F2<int>([]);
                Diagnostic(ErrorCode.ERR_RefReturnLvalueExpected, "[]").WithLocation(6, 28));
        }

        [Theory]
        [CombinatorialData]
        public void Span_05(bool useReadOnlySpan)
        {
            string spanType = useReadOnlySpan ? "ReadOnlySpan" : "Span";
            string source = $$"""
                using System;
                class Program
                {
                    static ref readonly {{spanType}}<int> F1()
                    {
                        return ref F2<int>([]);
                    }
                    static ref readonly {{spanType}}<T> F2<T>(scoped in {{spanType}}<T> s)
                    {
                        throw null;
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net70);
            comp.VerifyEmitDiagnostics();
        }

        [Fact]
        public void Span_MissingConstructor()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        Span<string> x = [];
                        ReadOnlySpan<int> y = [1, 2, 3];
                    }
                }
                """;

            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net70);
            comp.MakeMemberMissing(WellKnownMember.System_Span_T__ctor_Array);
            comp.VerifyEmitDiagnostics(
                // (6,26): error CS0656: Missing compiler required member 'System.Span`1..ctor'
                //         Span<string> x = [];
                Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "[]").WithArguments("System.Span`1", ".ctor").WithLocation(6, 26));

            comp = CreateCompilation(source, targetFramework: TargetFramework.Net70);
            comp.MakeMemberMissing(WellKnownMember.System_ReadOnlySpan_T__ctor_Array);
            comp.VerifyEmitDiagnostics(
                // (7,31): error CS0656: Missing compiler required member 'System.ReadOnlySpan`1..ctor'
                //         ReadOnlySpan<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "[1, 2, 3]").WithArguments("System.ReadOnlySpan`1", ".ctor").WithLocation(7, 31));
        }

        [Fact]
        public void InterfaceType()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Runtime.InteropServices;

                [ComImport]
                [Guid("1FC6664D-C61E-4131-81CD-A3EE0DD6098F")]
                [CoClass(typeof(C))]
                interface I : IEnumerable
                {
                    void Add(int i);
                }

                class C : I
                {
                    IEnumerator IEnumerable.GetEnumerator() => null;
                    void I.Add(int i) { }
                }

                class Program
                {
                    static void Main()
                    {
                        I i;
                        i = new() { };
                        i = new() { 1, 2 };
                        i = [];
                        i = [3, 4];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (26,13): error CS9174: Cannot initialize type 'I' with a collection expression because the type is not constructible.
                //         i = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("I").WithLocation(26, 13),
                // (27,13): error CS9174: Cannot initialize type 'I' with a collection expression because the type is not constructible.
                //         i = [3, 4];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[3, 4]").WithArguments("I").WithLocation(27, 13));
        }

        [Fact]
        public void EnumType_01()
        {
            string source = """
                enum E { }
                class Program
                {
                    static void Main()
                    {
                        E e;
                        e = [];
                        e = [1, 2];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (7,13): error CS9174: Cannot initialize type 'E' with a collection expression because the type is not constructible.
                //         e = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("E").WithLocation(7, 13),
                // (8,13): error CS9174: Cannot initialize type 'E' with a collection expression because the type is not constructible.
                //         e = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("E").WithLocation(8, 13));
        }

        [Fact]
        public void EnumType_02()
        {
            string sourceA = """
                using System.Collections;
                namespace System
                {
                    public class Object { }
                    public abstract class ValueType { }
                    public class String { }
                    public class Type { }
                    public struct Void { }
                    public struct Boolean { }
                    public struct Int32 { }
                    public struct Enum : IEnumerable { }
                }
                namespace System.Collections
                {
                    public interface IEnumerable { }
                }
                namespace System.Collections.Generic
                {
                    public class List<T> : IEnumerable { }
                }
                """;
            string sourceB = """
                enum E { }
                class Program
                {
                    static void Main()
                    {
                        E e;
                        e = [];
                        e = [1, 2];
                    }
                }
                """;
            var comp = CreateEmptyCompilation(new[] { sourceA, sourceB }, parseOptions: TestOptions.RegularPreview.WithNoRefSafetyRulesAttribute());
            // ConversionsBase.GetConstructibleCollectionType() ignores whether the enum
            // implements IEnumerable, so the type is not considered constructible.
            comp.VerifyEmitDiagnostics(
                // warning CS8021: No value for RuntimeMetadataVersion found. No assembly containing System.Object was found nor was a value for RuntimeMetadataVersion specified through options.
                Diagnostic(ErrorCode.WRN_NoRuntimeMetadataVersion).WithLocation(1, 1),
                // 1.cs(7,13): error CS9174: Cannot initialize type 'E' with a collection expression because the type is not constructible.
                //         e = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("E").WithLocation(7, 13),
                // 1.cs(8,13): error CS9174: Cannot initialize type 'E' with a collection expression because the type is not constructible.
                //         e = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("E").WithLocation(8, 13));
        }

        [Fact]
        public void DelegateType_01()
        {
            string source = """
                delegate void D();
                class Program
                {
                    static void Main()
                    {
                        D d;
                        d = [];
                        d = [1, 2];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (7,13): error CS9174: Cannot initialize type 'D' with a collection expression because the type is not constructible.
                //         d = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("D").WithLocation(7, 13),
                // (8,13): error CS9174: Cannot initialize type 'D' with a collection expression because the type is not constructible.
                //         d = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("D").WithLocation(8, 13));
        }

        [Fact]
        public void DelegateType_02()
        {
            string sourceA = """
                using System.Collections;
                namespace System
                {
                    public class Object { }
                    public abstract class ValueType { }
                    public class String { }
                    public class Type { }
                    public struct Void { }
                    public struct Boolean { }
                    public struct Int32 { }
                    public struct IntPtr { }
                    public abstract class Delegate : IEnumerable { }
                    public abstract class MulticastDelegate : Delegate { }
                }
                namespace System.Collections
                {
                    public interface IEnumerable { }
                }
                namespace System.Collections.Generic
                {
                    public class List<T> : IEnumerable { }
                }
                """;
            string sourceB = """
                delegate void D();
                class Program
                {
                    static void Main()
                    {
                        D d;
                        d = [];
                        d = [1, 2];
                    }
                }
                """;
            var comp = CreateEmptyCompilation(new[] { sourceA, sourceB }, parseOptions: TestOptions.RegularPreview.WithNoRefSafetyRulesAttribute());
            // ConversionsBase.GetConstructibleCollectionType() ignores whether the delegate
            // implements IEnumerable, so the type is not considered constructible.
            comp.VerifyEmitDiagnostics(
                // warning CS8021: No value for RuntimeMetadataVersion found. No assembly containing System.Object was found nor was a value for RuntimeMetadataVersion specified through options.
                Diagnostic(ErrorCode.WRN_NoRuntimeMetadataVersion).WithLocation(1, 1),
                // 1.cs(7,13): error CS9174: Cannot initialize type 'D' with a collection expression because the type is not constructible.
                //         d = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("D").WithLocation(7, 13),
                // 1.cs(8,13): error CS9174: Cannot initialize type 'D' with a collection expression because the type is not constructible.
                //         d = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("D").WithLocation(8, 13));
        }

        [Fact]
        public void PointerType_01()
        {
            string source = """
                class Program
                {
                    unsafe static void Main()
                    {
                        int* x = [];
                        int* y = [1, 2];
                        var z = (int*)[3];
                    }
                }
                """;
            var comp = CreateCompilation(source, options: TestOptions.UnsafeReleaseExe);
            comp.VerifyEmitDiagnostics(
                // (5,18): error CS9174: Cannot initialize type 'int*' with a collection expression because the type is not constructible.
                //         int* x = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("int*").WithLocation(5, 18),
                // (6,18): error CS9174: Cannot initialize type 'int*' with a collection expression because the type is not constructible.
                //         int* y = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("int*").WithLocation(6, 18),
                // (7,17): error CS9174: Cannot initialize type 'int*' with a collection expression because the type is not constructible.
                //         var z = (int*)[3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "(int*)[3]").WithArguments("int*").WithLocation(7, 17));
        }

        [Fact]
        public void PointerType_02()
        {
            string source = """
                class Program
                {
                    unsafe static void Main()
                    {
                        delegate*<void> x = [];
                        delegate*<void> y = [1, 2];
                        var z = (delegate*<void>)[3];
                    }
                }
                """;
            var comp = CreateCompilation(source, options: TestOptions.UnsafeReleaseExe);
            comp.VerifyEmitDiagnostics(
                // (5,29): error CS9174: Cannot initialize type 'delegate*<void>' with a collection expression because the type is not constructible.
                //         delegate*<void> x = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("delegate*<void>").WithLocation(5, 29),
                // (6,29): error CS9174: Cannot initialize type 'delegate*<void>' with a collection expression because the type is not constructible.
                //         delegate*<void> y = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("delegate*<void>").WithLocation(6, 29),
                // (7,17): error CS9174: Cannot initialize type 'delegate*<void>' with a collection expression because the type is not constructible.
                //         var z = (delegate*<void>)[3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "(delegate*<void>)[3]").WithArguments("delegate*<void>").WithLocation(7, 17));
        }

        [Fact]
        public void PointerType_03()
        {
            string source = """
                class Program
                {
                    unsafe static void Main()
                    {
                        void* p = null;
                        delegate*<void> d = null;
                        var x = [p];
                        var y = [d];
                    }
                }
                """;
            var comp = CreateCompilation(source, options: TestOptions.UnsafeReleaseExe);
            comp.VerifyEmitDiagnostics(
                // (7,17): error CS9176: There is no target type for the collection expression.
                //         var x = [p];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[p]").WithLocation(7, 17),
                // (8,17): error CS9176: There is no target type for the collection expression.
                //         var y = [d];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[d]").WithLocation(8, 17));
        }

        [Fact]
        public void PointerType_04()
        {
            string source = """
                using System;
                class Program
                {
                    unsafe static void Main()
                    {
                        void*[] a = [null, (void*)2];
                        foreach (void* p in a)
                            Console.Write("{0}, ", (nint)p);
                    }
                }
                """;
            CompileAndVerify(source, options: TestOptions.UnsafeReleaseExe, verify: Verification.Skipped, expectedOutput: "0, 2, ");
        }

        [Fact]
        public void PointerType_05()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C : IEnumerable
                {
                    private List<nint> _list = new List<nint>();
                    unsafe public void Add(void* p) { _list.Add((nint)p); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    unsafe static void Main()
                    {
                        void* p = (void*)2;
                        C c = [null, p];
                        c.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, options: TestOptions.UnsafeReleaseExe, verify: Verification.Skipped, expectedOutput: "[0, 2], ");
        }

        [Fact]
        public void CollectionInitializerType_01()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static List<int> Create1() => [];
                    static List<object> Create2() => [1, 2];
                    static List<int> Create3() => [3, 4, 5];
                    static List<long?> Create4() => [null, 7];
                    static void Main()
                    {
                        Create1().Report();
                        Create2().Report();
                        Create3().Report();
                        Create4().Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [1, 2], [3, 4, 5], [null, 7], ");
            verifier.VerifyIL("Program.Create1", """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  newobj     "System.Collections.Generic.List<int>..ctor()"
                  IL_0005:  ret
                }
                """);
            verifier.VerifyIL("Program.Create2", """
                {
                  // Code size       30 (0x1e)
                  .maxstack  3
                  IL_0000:  newobj     "System.Collections.Generic.List<object>..ctor()"
                  IL_0005:  dup
                  IL_0006:  ldc.i4.1
                  IL_0007:  box        "int"
                  IL_000c:  callvirt   "void System.Collections.Generic.List<object>.Add(object)"
                  IL_0011:  dup
                  IL_0012:  ldc.i4.2
                  IL_0013:  box        "int"
                  IL_0018:  callvirt   "void System.Collections.Generic.List<object>.Add(object)"
                  IL_001d:  ret
                }
                """);
            verifier.VerifyIL("Program.Create3", """
                {
                  // Code size       27 (0x1b)
                  .maxstack  3
                  IL_0000:  newobj     "System.Collections.Generic.List<int>..ctor()"
                  IL_0005:  dup
                  IL_0006:  ldc.i4.3
                  IL_0007:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                  IL_000c:  dup
                  IL_000d:  ldc.i4.4
                  IL_000e:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                  IL_0013:  dup
                  IL_0014:  ldc.i4.5
                  IL_0015:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                  IL_001a:  ret
                }
                """);
            verifier.VerifyIL("Program.Create4", """
                {
                  // Code size       34 (0x22)
                  .maxstack  3
                  .locals init (long? V_0)
                  IL_0000:  newobj     "System.Collections.Generic.List<long?>..ctor()"
                  IL_0005:  dup
                  IL_0006:  ldloca.s   V_0
                  IL_0008:  initobj    "long?"
                  IL_000e:  ldloc.0
                  IL_000f:  callvirt   "void System.Collections.Generic.List<long?>.Add(long?)"
                  IL_0014:  dup
                  IL_0015:  ldc.i4.7
                  IL_0016:  conv.i8
                  IL_0017:  newobj     "long?..ctor(long)"
                  IL_001c:  callvirt   "void System.Collections.Generic.List<long?>.Add(long?)"
                  IL_0021:  ret
                }
                """);
        }

        [Fact]
        public void CollectionInitializerType_02()
        {
            string source = """
                S s;
                s = [];
                s = [1, 2];
                s = [default];
                s = [Unknown];
                struct S { }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (2,5): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                // s = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("S").WithLocation(2, 5),
                // (3,5): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                // s = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("S").WithLocation(3, 5),
                // (4,5): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                // s = [default];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[default]").WithArguments("S").WithLocation(4, 5),
                // (5,6): error CS0103: The name 'Unknown' does not exist in the current context
                // s = [Unknown];
                Diagnostic(ErrorCode.ERR_NameNotInContext, "Unknown").WithArguments("Unknown").WithLocation(5, 6));
        }

        [Fact]
        public void CollectionInitializerType_03()
        {
            string source = """
                using System.Collections;
                S s;
                s = [];
                struct S : IEnumerable
                {
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                """;
            CompileAndVerify(source, expectedOutput: "");

            source = """
                using System.Collections;
                S s;
                s = [1, 2];
                struct S : IEnumerable
                {
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (3,6): error CS1061: 'S' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'S' could be found (are you missing a using directive or an assembly reference?)
                // s = [1, 2];
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "1").WithArguments("S", "Add").WithLocation(3, 6),
                // (3,9): error CS1061: 'S' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'S' could be found (are you missing a using directive or an assembly reference?)
                // s = [1, 2];
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "2").WithArguments("S", "Add").WithLocation(3, 9));
        }

        [Fact]
        public void CollectionInitializerType_04()
        {
            string source = """
                using System.Collections;
                C c;
                c = [];
                c = [1, 2];
                class C : IEnumerable
                {
                    C(object o) { }
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                    public void Add(int i) { }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (3,5): error CS1729: 'C' does not contain a constructor that takes 0 arguments
                // c = [];
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "[]").WithArguments("C", "0").WithLocation(3, 5),
                // (4,5): error CS1729: 'C' does not contain a constructor that takes 0 arguments
                // c = [1, 2];
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "[1, 2]").WithArguments("C", "0").WithLocation(4, 5));
        }

        [Fact]
        public void CollectionInitializerType_05()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class A : IEnumerable<int>
                {
                    A() { }
                    public void Add(int i) { }
                    IEnumerator<int> IEnumerable<int>.GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                    static A Create1() => [];
                }
                class B
                {
                    static A Create2() => [1, 2];
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (13,27): error CS0122: 'A.A()' is inaccessible due to its protection level
                //     static A Create2() => [1, 2];
                Diagnostic(ErrorCode.ERR_BadAccess, "[1, 2]").WithArguments("A.A()").WithLocation(13, 27));
        }

        [Fact]
        public void CollectionInitializerType_06()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C<T> : IEnumerable
                {
                    private List<T> _list = new List<T>();
                    public void Add(T t) { _list.Add(t); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C<int> c;
                        object o;
                        c = [];
                        o = (C<object>)[];
                        c.Report();
                        o.Report();
                        c = [1, 2];
                        o = (C<object>)[3, 4];
                        c.Report();
                        o.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [], [1, 2], [3, 4], ");
        }

        [Fact]
        public void CollectionInitializerType_ConstructorOptionalParameters()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C : IEnumerable<int>
                {
                    private List<int> _list = new List<int>();
                    internal C(int x = 1, int y = 2) { }
                    public void Add(int i) { _list.Add(i); }
                    IEnumerator<int> IEnumerable<int>.GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C c;
                        object o;
                        c = [];
                        o = (C)([]);
                        c.Report();
                        o.Report();
                        c = [1, 2];
                        o = (C)([3, 4]);
                        c.Report();
                        o.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [], [1, 2], [3, 4], ");
        }

        [Fact]
        public void CollectionInitializerType_ConstructorParamsArray()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C : IEnumerable<int>
                {
                    private List<int> _list = new List<int>();
                    internal C(params int[] args) { }
                    public void Add(int i) { _list.Add(i); }
                    IEnumerator<int> IEnumerable<int>.GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C c;
                        object o;
                        c = [];
                        o = (C)([]);
                        c.Report();
                        o.Report();
                        c = [1, 2];
                        o = (C)([3, 4]);
                        c.Report();
                        o.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [], [1, 2], [3, 4], ");
        }

        [Fact]
        public void CollectionInitializerType_07()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                abstract class A : IEnumerable<int>
                {
                    public void Add(int i) { }
                    IEnumerator<int> IEnumerable<int>.GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }
                class B : A { }
                class Program
                {
                    static void Main()
                    {
                        A a = [];
                        B b = [];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (14,15): error CS0144: Cannot create an instance of the abstract type or interface 'A'
                //         A a = [];
                Diagnostic(ErrorCode.ERR_NoNewAbstract, "[]").WithArguments("A").WithLocation(14, 15));
        }

        [Fact]
        public void CollectionInitializerType_08()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                struct S0<T> : IEnumerable
                {
                    public void Add(T t) { }
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                struct S1<T> : IEnumerable<T>
                {
                    public void Add(T t) { }
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw null;
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                struct S2<T> : IEnumerable<T>
                {
                    public S2() { }
                    public void Add(T t) { }
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw null;
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                class Program
                {
                    static void M0()
                    {
                        object o = (S0<int>)[];
                        S0<int> s = [1, 2];
                    }
                    static void M1()
                    {
                        object o = (S1<int>)[];
                        S1<int> s = [1, 2];
                    }
                    static void M2()
                    {
                        S2<int> s = [];
                        object o = (S2<int>)[1, 2];
                    }
                }
                """;
            var verifier = CompileAndVerify(source);
            verifier.VerifyIL("Program.M0", """
                {
                  // Code size       35 (0x23)
                  .maxstack  2
                  .locals init (S0<int> V_0)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "S0<int>"
                  IL_0008:  ldloc.0
                  IL_0009:  pop
                  IL_000a:  ldloca.s   V_0
                  IL_000c:  initobj    "S0<int>"
                  IL_0012:  ldloca.s   V_0
                  IL_0014:  ldc.i4.1
                  IL_0015:  call       "void S0<int>.Add(int)"
                  IL_001a:  ldloca.s   V_0
                  IL_001c:  ldc.i4.2
                  IL_001d:  call       "void S0<int>.Add(int)"
                  IL_0022:  ret
                }
                """);
            verifier.VerifyIL("Program.M1", """
                {
                  // Code size       35 (0x23)
                  .maxstack  2
                  .locals init (S1<int> V_0)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "S1<int>"
                  IL_0008:  ldloc.0
                  IL_0009:  pop
                  IL_000a:  ldloca.s   V_0
                  IL_000c:  initobj    "S1<int>"
                  IL_0012:  ldloca.s   V_0
                  IL_0014:  ldc.i4.1
                  IL_0015:  call       "void S1<int>.Add(int)"
                  IL_001a:  ldloca.s   V_0
                  IL_001c:  ldc.i4.2
                  IL_001d:  call       "void S1<int>.Add(int)"
                  IL_0022:  ret
                }
                """);
            verifier.VerifyIL("Program.M2", """
                {
                  // Code size       30 (0x1e)
                  .maxstack  2
                  .locals init (S2<int> V_0)
                  IL_0000:  newobj     "S2<int>..ctor()"
                  IL_0005:  pop
                  IL_0006:  ldloca.s   V_0
                  IL_0008:  call       "S2<int>..ctor()"
                  IL_000d:  ldloca.s   V_0
                  IL_000f:  ldc.i4.1
                  IL_0010:  call       "void S2<int>.Add(int)"
                  IL_0015:  ldloca.s   V_0
                  IL_0017:  ldc.i4.2
                  IL_0018:  call       "void S2<int>.Add(int)"
                  IL_001d:  ret
                }
                """);
        }

        [Fact]
        public void CollectionInitializerType_09()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        UnknownType u;
                        u = [];
                        u = [null, B];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (7,9): error CS0246: The type or namespace name 'UnknownType' could not be found (are you missing a using directive or an assembly reference?)
                //         UnknownType u;
                Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "UnknownType").WithArguments("UnknownType").WithLocation(7, 9),
                // (9,20): error CS0103: The name 'B' does not exist in the current context
                //         u = [null, B];
                Diagnostic(ErrorCode.ERR_NameNotInContext, "B").WithArguments("B").WithLocation(9, 20));
        }

        [Fact]
        public void CollectionInitializerType_10()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                struct S<T> : IEnumerable<string>
                {
                    public void Add(string i) { }
                    IEnumerator<string> IEnumerable<string>.GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }
                class Program
                {
                    static void Main()
                    {
                        S<UnknownType> s;
                        s = [];
                        s = [null, B];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (13,11): error CS0246: The type or namespace name 'UnknownType' could not be found (are you missing a using directive or an assembly reference?)
                //         S<UnknownType> s;
                Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "UnknownType").WithArguments("UnknownType").WithLocation(13, 11),
                // (15,20): error CS0103: The name 'B' does not exist in the current context
                //         s = [null, B];
                Diagnostic(ErrorCode.ERR_NameNotInContext, "B").WithArguments("B").WithLocation(15, 20));
        }

        [Fact]
        public void CollectionInitializerType_11()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        List<List<int>> l;
                        l = [[], [2, 3]];
                        l = [[], {2, 3}];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (8,18): error CS1003: Syntax error, ']' expected
                //         l = [[], {2, 3}];
                Diagnostic(ErrorCode.ERR_SyntaxError, "{").WithArguments("]").WithLocation(8, 18),
                // (8,18): error CS1002: ; expected
                //         l = [[], {2, 3}];
                Diagnostic(ErrorCode.ERR_SemicolonExpected, "{").WithLocation(8, 18),
                // (8,20): error CS1002: ; expected
                //         l = [[], {2, 3}];
                Diagnostic(ErrorCode.ERR_SemicolonExpected, ",").WithLocation(8, 20),
                // (8,20): error CS1513: } expected
                //         l = [[], {2, 3}];
                Diagnostic(ErrorCode.ERR_RbraceExpected, ",").WithLocation(8, 20),
                // (8,23): error CS1002: ; expected
                //         l = [[], {2, 3}];
                Diagnostic(ErrorCode.ERR_SemicolonExpected, "}").WithLocation(8, 23),
                // (8,24): error CS1513: } expected
                //         l = [[], {2, 3}];
                Diagnostic(ErrorCode.ERR_RbraceExpected, "]").WithLocation(8, 24));
        }

        [Fact]
        public void CollectionInitializerType_12()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C : IEnumerable
                {
                    List<string> _list = new List<string>();
                    public void Add(int i) { _list.Add($"i={i}"); }
                    public void Add(object o) { _list.Add($"o={o}"); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C x = [];
                        C y = [1, (object)2];
                        x.Report();
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [i=1, o=2], ");
        }

        [Fact]
        public void CollectionInitializerType_13()
        {
            string source = """
                using System.Collections;
                interface IA { }
                interface IB { }
                class AB : IA, IB { }
                class C : IEnumerable
                {
                    public void Add(IA a) { }
                    public void Add(IB b) { }
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }
                class Program
                {
                    static void Main()
                    {
                        C c = [(IA)null, (IB)null, new AB()];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (15,36): error CS0121: The call is ambiguous between the following methods or properties: 'C.Add(IA)' and 'C.Add(IB)'
                //         C c = [(IA)null, (IB)null, new AB()];
                Diagnostic(ErrorCode.ERR_AmbigCall, "new AB()").WithArguments("C.Add(IA)", "C.Add(IB)").WithLocation(15, 36));
        }

        [Fact]
        public void CollectionInitializerType_14()
        {
            string source = """
                using System.Collections;
                struct S<T> : IEnumerable
                {
                    public void Add(T x, T y) { }
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                class Program
                {
                    static void Main()
                    {
                        S<int> s;
                        s = [];
                        s = [1, 2];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (13,14): error CS7036: There is no argument given that corresponds to the required parameter 'y' of 'S<int>.Add(int, int)'
                //         s = [1, 2];
                Diagnostic(ErrorCode.ERR_NoCorrespondingArgument, "1").WithArguments("y", "S<int>.Add(int, int)").WithLocation(13, 14),
                // (13,17): error CS7036: There is no argument given that corresponds to the required parameter 'y' of 'S<int>.Add(int, int)'
                //         s = [1, 2];
                Diagnostic(ErrorCode.ERR_NoCorrespondingArgument, "2").WithArguments("y", "S<int>.Add(int, int)").WithLocation(13, 17));
        }

        [Fact]
        public void CollectionInitializerType_15()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C<T> : IEnumerable
                {
                    List<T> _list = new List<T>();
                    public void Add(T t, int index = -1) { _list.Add(t); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C<int> c = [1, 2];
                        c.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2], ");
        }

        [Fact]
        public void CollectionInitializerType_16()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C<T> : IEnumerable
                {
                    List<T> _list = new List<T>();
                    public void Add(T t, params T[] args) { _list.Add(t); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C<int> c = [1, 2];
                        c.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2], ");
        }

        [Fact]
        public void CollectionInitializerType_17()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C<T> : IEnumerable
                {
                    List<T> _list = new List<T>();
                    public void Add(params T[] args) { _list.AddRange(args); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C<int> c = [[], [1, 2], 3];
                        c.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2, 3], ");
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size       61 (0x3d)
                  .maxstack  5
                  .locals init (C<int> V_0)
                  IL_0000:  newobj     "C<int>..ctor()"
                  IL_0005:  stloc.0
                  IL_0006:  ldloc.0
                  IL_0007:  call       "int[] System.Array.Empty<int>()"
                  IL_000c:  callvirt   "void C<int>.Add(params int[])"
                  IL_0011:  ldloc.0
                  IL_0012:  ldc.i4.2
                  IL_0013:  newarr     "int"
                  IL_0018:  dup
                  IL_0019:  ldc.i4.0
                  IL_001a:  ldc.i4.1
                  IL_001b:  stelem.i4
                  IL_001c:  dup
                  IL_001d:  ldc.i4.1
                  IL_001e:  ldc.i4.2
                  IL_001f:  stelem.i4
                  IL_0020:  callvirt   "void C<int>.Add(params int[])"
                  IL_0025:  ldloc.0
                  IL_0026:  ldc.i4.1
                  IL_0027:  newarr     "int"
                  IL_002c:  dup
                  IL_002d:  ldc.i4.0
                  IL_002e:  ldc.i4.3
                  IL_002f:  stelem.i4
                  IL_0030:  callvirt   "void C<int>.Add(params int[])"
                  IL_0035:  ldloc.0
                  IL_0036:  ldc.i4.0
                  IL_0037:  call       "void CollectionExtensions.Report(object, bool)"
                  IL_003c:  ret
                }
                """);
        }

        [Fact]
        public void CollectionInitializerType_18()
        {
            string source = """
                using System.Collections;
                class S<T, U> : IEnumerable
                {
                    internal void Add(T t) { }
                    private void Add(U u) { }
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                    static S<T, U> Create(T t, U u) => [t, u];
                }
                class Program
                {
                    static S<T, U> Create<T, U>(T x, U y) => [x, y];
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (11,50): error CS1950: The best overloaded Add method 'S<T, U>.Add(T)' for the collection initializer has some invalid arguments
                //     static S<T, U> Create<T, U>(T x, U y) => [x, y];
                Diagnostic(ErrorCode.ERR_BadArgTypesForCollectionAdd, "y").WithArguments("S<T, U>.Add(T)").WithLocation(11, 50),
                // (11,50): error CS1503: Argument 1: cannot convert from 'U' to 'T'
                //     static S<T, U> Create<T, U>(T x, U y) => [x, y];
                Diagnostic(ErrorCode.ERR_BadArgType, "y").WithArguments("1", "U", "T").WithLocation(11, 50));
        }

        [Fact]
        public void CollectionInitializerType_19()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        string s;
                        s = [];
                        s = ['a'];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,13): error CS1729: 'string' does not contain a constructor that takes 0 arguments
                //         s = [];
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "[]").WithArguments("string", "0").WithLocation(6, 13),
                // (7,13): error CS1729: 'string' does not contain a constructor that takes 0 arguments
                //         s = ['a'];
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "['a']").WithArguments("string", "0").WithLocation(7, 13),
                // (7,14): error CS1061: 'string' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'string' could be found (are you missing a using directive or an assembly reference?)
                //         s = ['a'];
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "'a'").WithArguments("string", "Add").WithLocation(7, 14));
        }

        [Theory]
        [InlineData("class")]
        [InlineData("struct")]
        public void TypeParameter_01(string type)
        {
            string source = $$"""
                using System;
                using System.Collections;
                using System.Collections.Generic;
                interface I<T> : IEnumerable
                {
                    void Add(T t);
                }
                {{type}} C<T> : I<T>
                {
                    private List<T> _list;
                    public void Add(T t)
                    {
                        GetList().Add(t);
                    }
                    IEnumerator IEnumerable.GetEnumerator()
                    {
                        return GetList().GetEnumerator();
                    }
                    private List<T> GetList() => _list ??= new List<T>();
                }
                class Program
                {
                    static void Main()
                    {
                        CreateEmpty<C<object>, object>().Report();
                        Create<C<long?>, long?>(null, 2).Report();
                    }
                    static T CreateEmpty<T, U>() where T : I<U>, new()
                    {
                        return [];
                    }
                    static T Create<T, U>(U a, U b) where T : I<U>, new()
                    {
                        return [a, b];
                    }
                }
                """;
            var verifier = CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[], [null, 2], ");
            verifier.VerifyIL("Program.CreateEmpty<T, U>", """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  call       "T System.Activator.CreateInstance<T>()"
                  IL_0005:  ret
                }
                """);
            verifier.VerifyIL("Program.Create<T, U>", """
                {
                  // Code size       36 (0x24)
                  .maxstack  2
                  .locals init (T V_0)
                  IL_0000:  call       "T System.Activator.CreateInstance<T>()"
                  IL_0005:  stloc.0
                  IL_0006:  ldloca.s   V_0
                  IL_0008:  ldarg.0
                  IL_0009:  constrained. "T"
                  IL_000f:  callvirt   "void I<U>.Add(U)"
                  IL_0014:  ldloca.s   V_0
                  IL_0016:  ldarg.1
                  IL_0017:  constrained. "T"
                  IL_001d:  callvirt   "void I<U>.Add(U)"
                  IL_0022:  ldloc.0
                  IL_0023:  ret
                }
                """);
        }

        [Fact]
        public void TypeParameter_02()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                interface I<T> : IEnumerable<T>
                {
                    void Add(T t);
                }
                struct S<T> : I<T>
                {
                    public void Add(T t) { }
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw null;
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                class Program
                {
                    static T Create1<T, U>() where T : struct, I<U> => [];
                    static T? Create2<T, U>() where T : struct, I<U> => [];
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (16,57): error CS9174: Cannot initialize type 'T?' with a collection expression because the type is not constructible.
                //     static T? Create2<T, U>() where T : struct, I<U> => [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("T?").WithLocation(16, 57));
        }

        [Fact]
        public void TypeParameter_03()
        {
            string source = """
                using System.Collections;
                class Program
                {
                    static T Create1<T, U>() where T : IEnumerable => []; // 1
                    static T Create2<T, U>() where T : class, IEnumerable => []; // 2
                    static T Create3<T, U>() where T : struct, IEnumerable => [];
                    static T Create4<T, U>() where T : IEnumerable, new() => [];
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (4,55): error CS0304: Cannot create an instance of the variable type 'T' because it does not have the new() constraint
                //     static T Create1<T, U>() where T : IEnumerable => []; // 1
                Diagnostic(ErrorCode.ERR_NoNewTyvar, "[]").WithArguments("T").WithLocation(4, 55),
                // (5,62): error CS0304: Cannot create an instance of the variable type 'T' because it does not have the new() constraint
                //     static T Create2<T, U>() where T : class, IEnumerable => []; // 2
                Diagnostic(ErrorCode.ERR_NoNewTyvar, "[]").WithArguments("T").WithLocation(5, 62));
        }

        [Fact]
        public void TypeParameter_04()
        {
            string source = """
                using System.Collections;
                interface IAdd : IEnumerable
                {
                    void Add(int i);
                }
                class Program
                {
                    static T Create1<T>() where T : IAdd => [1]; // 1
                    static T Create2<T>() where T : class, IAdd => [2]; // 2
                    static T Create3<T>() where T : struct, IAdd => [3];
                    static T Create4<T>() where T : IAdd, new() => [4];
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (8,45): error CS0304: Cannot create an instance of the variable type 'T' because it does not have the new() constraint
                //     static T Create1<T>() where T : IAdd => [1]; // 1
                Diagnostic(ErrorCode.ERR_NoNewTyvar, "[1]").WithArguments("T").WithLocation(8, 45),
                // (9,52): error CS0304: Cannot create an instance of the variable type 'T' because it does not have the new() constraint
                //     static T Create2<T>() where T : class, IAdd => [2]; // 2
                Diagnostic(ErrorCode.ERR_NoNewTyvar, "[2]").WithArguments("T").WithLocation(9, 52));
        }

        [Fact]
        public void CollectionInitializerType_MissingIEnumerable()
        {
            string source = """
                struct S
                {
                }
                class Program
                {
                    static void Main()
                    {
                        S s = [];
                        object o = (S)([1, 2]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.MakeTypeMissing(SpecialType.System_Collections_IEnumerable);
            comp.VerifyEmitDiagnostics(
                // (8,15): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         S s = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("S").WithLocation(8, 15),
                // (9,20): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         object o = (S)([1, 2]);
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "(S)([1, 2])").WithArguments("S").WithLocation(9, 20));
        }

        [Fact]
        public void CollectionInitializerType_UseSiteErrors()
        {
            string assemblyA = GetUniqueName();
            string sourceA = """
                public class A1 { }
                public class A2 { }
                """;
            var comp = CreateCompilation(sourceA, assemblyName: assemblyA);
            var refA = comp.EmitToImageReference();

            string sourceB = """
                using System.Collections;
                using System.Collections.Generic;
                public class B1 : IEnumerable
                {
                    List<int> _list = new List<int>();
                    public B1(A1 a = null) { }
                    public void Add(int i) { _list.Add(i); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                public class B2 : IEnumerable
                {
                    List<int> _list = new List<int>();
                    public void Add(int x, A2 y = null) { _list.Add(x); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA });
            var refB = comp.EmitToImageReference();

            string sourceC = """
                class C
                {
                    static void Main()
                    {
                        B1 x;
                        x = [];
                        x.Report();
                        x = [1, 2];
                        x.Report();
                        B2 y;
                        y = [];
                        y.Report();
                        y = [3, 4];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { sourceC, s_collectionExtensions }, references: new[] { refA, refB }, expectedOutput: "[], [1, 2], [], [3, 4], ");

            comp = CreateCompilation(new[] { sourceC, s_collectionExtensions }, references: new[] { refB });
            comp.VerifyEmitDiagnostics(
                // (6,13): error CS0012: The type 'A1' is defined in an assembly that is not referenced. You must add a reference to assembly 'a897d975-a839-4fff-828b-deccf9495adc, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.
                //         x = [];
                Diagnostic(ErrorCode.ERR_NoTypeDef, "[]").WithArguments("A1", $"{assemblyA}, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null").WithLocation(6, 13),
                // (8,13): error CS0012: The type 'A1' is defined in an assembly that is not referenced. You must add a reference to assembly 'a897d975-a839-4fff-828b-deccf9495adc, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.
                //         x = [1, 2];
                Diagnostic(ErrorCode.ERR_NoTypeDef, "[1, 2]").WithArguments("A1", $"{assemblyA}, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null").WithLocation(8, 13),
                // (13,14): error CS0012: The type 'A2' is defined in an assembly that is not referenced. You must add a reference to assembly 'a897d975-a839-4fff-828b-deccf9495adc, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.
                //         y = [3, 4];
                Diagnostic(ErrorCode.ERR_NoTypeDef, "3").WithArguments("A2", $"{assemblyA}, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null").WithLocation(13, 14),
                // (13,17): error CS0012: The type 'A2' is defined in an assembly that is not referenced. You must add a reference to assembly 'a897d975-a839-4fff-828b-deccf9495adc, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.
                //         y = [3, 4];
                Diagnostic(ErrorCode.ERR_NoTypeDef, "4").WithArguments("A2", $"{assemblyA}, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null").WithLocation(13, 17));
        }

        [Fact]
        public void ConditionalAdd()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                using System.Diagnostics;
                class C<T, U> : IEnumerable
                {
                    List<object> _list = new List<object>();
                    [Conditional("DEBUG")] internal void Add(T t) { _list.Add(t); }
                    internal void Add(U u) { _list.Add(u); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C<int, string> c = [1, "2", 3];
                        c.Report();
                    }
                }
                """;
            var parseOptions = TestOptions.RegularPreview;
            CompileAndVerify(new[] { source, s_collectionExtensions }, parseOptions: parseOptions.WithPreprocessorSymbols("DEBUG"), expectedOutput: "[1, 2, 3], ");
            CompileAndVerify(new[] { source, s_collectionExtensions }, parseOptions: parseOptions, expectedOutput: "[2], ");
        }

        [Fact]
        public void DictionaryElement_01()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        Dictionary<int, int> d;
                        d = [];
                        d = [new KeyValuePair<int, int>(1, 2)];
                        d = [3:4];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (8,14): error CS7036: There is no argument given that corresponds to the required parameter 'value' of 'Dictionary<int, int>.Add(int, int)'
                //         d = [new KeyValuePair<int, int>(1, 2)];
                Diagnostic(ErrorCode.ERR_NoCorrespondingArgument, "new KeyValuePair<int, int>(1, 2)").WithArguments("value", "System.Collections.Generic.Dictionary<int, int>.Add(int, int)").WithLocation(8, 14),
                // (9,15): error CS1003: Syntax error, ',' expected
                //         d = [3:4];
                Diagnostic(ErrorCode.ERR_SyntaxError, ":").WithArguments(",").WithLocation(9, 15),
                // (9,16): error CS1003: Syntax error, ',' expected
                //         d = [3:4];
                Diagnostic(ErrorCode.ERR_SyntaxError, "4").WithArguments(",").WithLocation(9, 16));
        }

        [Theory]
        [CombinatorialData]
        public void SpreadElement_01(
            [CombinatorialValues("IEnumerable<int>", "int[]", "List<int>", "Span<int>", "ReadOnlySpan<int>")] string spreadType,
            [CombinatorialValues("IEnumerable<int>", "int[]", "List<int>", "Span<int>", "ReadOnlySpan<int>")] string collectionType)
        {
            string source = $$"""
                using System;
                using System.Collections;
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        F([1, 2, 3]);
                    }
                    static void F({{spreadType}} x)
                    {
                        {{collectionType}} y = [..x];
                        y.Report();
                    }
                }
                """;

            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                options: TestOptions.ReleaseExe,
                targetFramework: TargetFramework.Net70,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));

            // Verify some of the cases.
            string expectedIL = (spreadType, collectionType) switch
            {
                ("IEnumerable<int>", "IEnumerable<int>") =>
                    """
                    {
                      // Code size       57 (0x39)
                      .maxstack  2
                      .locals init (System.Collections.Generic.List<int> V_0,
                                    System.Collections.Generic.IEnumerator<int> V_1,
                                    int V_2)
                      IL_0000:  newobj     "System.Collections.Generic.List<int>..ctor()"
                      IL_0005:  stloc.0
                      IL_0006:  ldarg.0
                      IL_0007:  callvirt   "System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<int>.GetEnumerator()"
                      IL_000c:  stloc.1
                      .try
                      {
                        IL_000d:  br.s       IL_001d
                        IL_000f:  ldloc.1
                        IL_0010:  callvirt   "int System.Collections.Generic.IEnumerator<int>.Current.get"
                        IL_0015:  stloc.2
                        IL_0016:  ldloc.0
                        IL_0017:  ldloc.2
                        IL_0018:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                        IL_001d:  ldloc.1
                        IL_001e:  callvirt   "bool System.Collections.IEnumerator.MoveNext()"
                        IL_0023:  brtrue.s   IL_000f
                        IL_0025:  leave.s    IL_0031
                      }
                      finally
                      {
                        IL_0027:  ldloc.1
                        IL_0028:  brfalse.s  IL_0030
                        IL_002a:  ldloc.1
                        IL_002b:  callvirt   "void System.IDisposable.Dispose()"
                        IL_0030:  endfinally
                      }
                      IL_0031:  ldloc.0
                      IL_0032:  ldc.i4.0
                      IL_0033:  call       "void CollectionExtensions.Report(object, bool)"
                      IL_0038:  ret
                    }
                    """,
                ("IEnumerable<int>", "int[]") =>
                    """
                    {
                      // Code size       62 (0x3e)
                      .maxstack  2
                      .locals init (System.Collections.Generic.List<int> V_0,
                                    System.Collections.Generic.IEnumerator<int> V_1,
                                    int V_2)
                      IL_0000:  newobj     "System.Collections.Generic.List<int>..ctor()"
                      IL_0005:  stloc.0
                      IL_0006:  ldarg.0
                      IL_0007:  callvirt   "System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<int>.GetEnumerator()"
                      IL_000c:  stloc.1
                      .try
                      {
                        IL_000d:  br.s       IL_001d
                        IL_000f:  ldloc.1
                        IL_0010:  callvirt   "int System.Collections.Generic.IEnumerator<int>.Current.get"
                        IL_0015:  stloc.2
                        IL_0016:  ldloc.0
                        IL_0017:  ldloc.2
                        IL_0018:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                        IL_001d:  ldloc.1
                        IL_001e:  callvirt   "bool System.Collections.IEnumerator.MoveNext()"
                        IL_0023:  brtrue.s   IL_000f
                        IL_0025:  leave.s    IL_0031
                      }
                      finally
                      {
                        IL_0027:  ldloc.1
                        IL_0028:  brfalse.s  IL_0030
                        IL_002a:  ldloc.1
                        IL_002b:  callvirt   "void System.IDisposable.Dispose()"
                        IL_0030:  endfinally
                      }
                      IL_0031:  ldloc.0
                      IL_0032:  callvirt   "int[] System.Collections.Generic.List<int>.ToArray()"
                      IL_0037:  ldc.i4.0
                      IL_0038:  call       "void CollectionExtensions.Report(object, bool)"
                      IL_003d:  ret
                    }
                    """,
                ("int[]", "int[]") =>
                    // https://github.com/dotnet/roslyn/issues/68785: Avoid intermediate List<T> if all spread elements have Length property.
                    """
                    {
                      // Code size       46 (0x2e)
                      .maxstack  2
                      .locals init (System.Collections.Generic.List<int> V_0,
                                    int[] V_1,
                                    int V_2,
                                    int V_3)
                      IL_0000:  newobj     "System.Collections.Generic.List<int>..ctor()"
                      IL_0005:  stloc.0
                      IL_0006:  ldarg.0
                      IL_0007:  stloc.1
                      IL_0008:  ldc.i4.0
                      IL_0009:  stloc.2
                      IL_000a:  br.s       IL_001b
                      IL_000c:  ldloc.1
                      IL_000d:  ldloc.2
                      IL_000e:  ldelem.i4
                      IL_000f:  stloc.3
                      IL_0010:  ldloc.0
                      IL_0011:  ldloc.3
                      IL_0012:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                      IL_0017:  ldloc.2
                      IL_0018:  ldc.i4.1
                      IL_0019:  add
                      IL_001a:  stloc.2
                      IL_001b:  ldloc.2
                      IL_001c:  ldloc.1
                      IL_001d:  ldlen
                      IL_001e:  conv.i4
                      IL_001f:  blt.s      IL_000c
                      IL_0021:  ldloc.0
                      IL_0022:  callvirt   "int[] System.Collections.Generic.List<int>.ToArray()"
                      IL_0027:  ldc.i4.0
                      IL_0028:  call       "void CollectionExtensions.Report(object, bool)"
                      IL_002d:  ret
                    }
                    """,
                ("ReadOnlySpan<int>", "ReadOnlySpan<int>") =>
                    // https://github.com/dotnet/roslyn/issues/68785: Avoid intermediate List<T> if all spread elements have Length property.
                    """
                    {
                      // Code size       62 (0x3e)
                      .maxstack  2
                      .locals init (System.ReadOnlySpan<int> V_0, //y
                                    System.Collections.Generic.List<int> V_1,
                                    System.ReadOnlySpan<int>.Enumerator V_2,
                                    int V_3)
                      IL_0000:  newobj     "System.Collections.Generic.List<int>..ctor()"
                      IL_0005:  stloc.1
                      IL_0006:  ldarga.s   V_0
                      IL_0008:  call       "System.ReadOnlySpan<int>.Enumerator System.ReadOnlySpan<int>.GetEnumerator()"
                      IL_000d:  stloc.2
                      IL_000e:  br.s       IL_0020
                      IL_0010:  ldloca.s   V_2
                      IL_0012:  call       "ref readonly int System.ReadOnlySpan<int>.Enumerator.Current.get"
                      IL_0017:  ldind.i4
                      IL_0018:  stloc.3
                      IL_0019:  ldloc.1
                      IL_001a:  ldloc.3
                      IL_001b:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                      IL_0020:  ldloca.s   V_2
                      IL_0022:  call       "bool System.ReadOnlySpan<int>.Enumerator.MoveNext()"
                      IL_0027:  brtrue.s   IL_0010
                      IL_0029:  ldloca.s   V_0
                      IL_002b:  ldloc.1
                      IL_002c:  callvirt   "int[] System.Collections.Generic.List<int>.ToArray()"
                      IL_0031:  call       "System.ReadOnlySpan<int>..ctor(int[])"
                      IL_0036:  ldloca.s   V_0
                      IL_0038:  call       "void CollectionExtensions.Report<int>(in System.ReadOnlySpan<int>)"
                      IL_003d:  ret
                    }
                    """,
                _ => null
            };
            if (expectedIL is { })
            {
                verifier.VerifyIL("Program.F", expectedIL);
            }
        }

        [Theory]
        [InlineData("int[]")]
        [InlineData("System.Collections.Generic.List<int>")]
        [InlineData("System.Span<int>")]
        [InlineData("System.ReadOnlySpan<int>")]
        public void SpreadElement_02(string collectionType)
        {
            string source = $$"""
                class Program
                {
                    static void Main()
                    {
                        {{collectionType}} c = [];
                        Append(c);
                    }
                    static void Append({{collectionType}} x)
                    {
                        {{collectionType}} y = [1, 2];
                        {{collectionType}} z = [..x, ..y];
                        z.Report();
                    }
                }
                """;

            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                options: TestOptions.ReleaseExe,
                targetFramework: TargetFramework.Net70,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2], "));

            if (collectionType == "System.ReadOnlySpan<int>")
            {
                verifier.VerifyIL("Program.Append",
                    """
                    {
                      // Code size      112 (0x70)
                      .maxstack  2
                      .locals init (System.ReadOnlySpan<int> V_0, //y
                                    System.ReadOnlySpan<int> V_1, //z
                                    System.Collections.Generic.List<int> V_2,
                                    System.ReadOnlySpan<int>.Enumerator V_3,
                                    int V_4)
                      IL_0000:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=8_Align=4 <PrivateImplementationDetails>.34FB5C825DE7CA4AEA6E712F19D439C1DA0C92C37B423936C5F618545CA4FA1F4"
                      IL_0005:  call       "System.ReadOnlySpan<int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<int>(System.RuntimeFieldHandle)"
                      IL_000a:  stloc.0
                      IL_000b:  newobj     "System.Collections.Generic.List<int>..ctor()"
                      IL_0010:  stloc.2
                      IL_0011:  ldarga.s   V_0
                      IL_0013:  call       "System.ReadOnlySpan<int>.Enumerator System.ReadOnlySpan<int>.GetEnumerator()"
                      IL_0018:  stloc.3
                      IL_0019:  br.s       IL_002d
                      IL_001b:  ldloca.s   V_3
                      IL_001d:  call       "ref readonly int System.ReadOnlySpan<int>.Enumerator.Current.get"
                      IL_0022:  ldind.i4
                      IL_0023:  stloc.s    V_4
                      IL_0025:  ldloc.2
                      IL_0026:  ldloc.s    V_4
                      IL_0028:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                      IL_002d:  ldloca.s   V_3
                      IL_002f:  call       "bool System.ReadOnlySpan<int>.Enumerator.MoveNext()"
                      IL_0034:  brtrue.s   IL_001b
                      IL_0036:  ldloca.s   V_0
                      IL_0038:  call       "System.ReadOnlySpan<int>.Enumerator System.ReadOnlySpan<int>.GetEnumerator()"
                      IL_003d:  stloc.3
                      IL_003e:  br.s       IL_0052
                      IL_0040:  ldloca.s   V_3
                      IL_0042:  call       "ref readonly int System.ReadOnlySpan<int>.Enumerator.Current.get"
                      IL_0047:  ldind.i4
                      IL_0048:  stloc.s    V_4
                      IL_004a:  ldloc.2
                      IL_004b:  ldloc.s    V_4
                      IL_004d:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                      IL_0052:  ldloca.s   V_3
                      IL_0054:  call       "bool System.ReadOnlySpan<int>.Enumerator.MoveNext()"
                      IL_0059:  brtrue.s   IL_0040
                      IL_005b:  ldloca.s   V_1
                      IL_005d:  ldloc.2
                      IL_005e:  callvirt   "int[] System.Collections.Generic.List<int>.ToArray()"
                      IL_0063:  call       "System.ReadOnlySpan<int>..ctor(int[])"
                      IL_0068:  ldloca.s   V_1
                      IL_006a:  call       "void CollectionExtensions.Report<int>(in System.ReadOnlySpan<int>)"
                      IL_006f:  ret
                    }
                    """);
            }
        }

        [Fact]
        public void SpreadElement_03()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                struct S<T> : IEnumerable<T>
                {
                    private List<T> _list;
                    public void Add(T t)
                    {
                        _list ??= new List<T>();
                        _list.Add(t);
                    }
                    public IEnumerator<T> GetEnumerator()
                    {
                        _list ??= new List<T>();
                        return _list.GetEnumerator();
                    }
                    IEnumerator IEnumerable.GetEnumerator()
                    {
                        return GetEnumerator();
                    }
                }
                class Program
                {
                    static void Main()
                    {
                        S<int> s;
                        s = [];
                        s = Append(s);
                        s.Report();
                    }
                    static S<int> Append(S<int> x)
                    {
                        S<int> y = [1, 2];
                        return [..x, ..y];
                    }
                }
                """;

            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                options: TestOptions.ReleaseExe,
                expectedOutput: "[1, 2], ");

            verifier.VerifyIL("Program.Append",
                """
                {
                  // Code size      126 (0x7e)
                  .maxstack  2
                  .locals init (S<int> V_0, //y
                                S<int> V_1,
                                System.Collections.Generic.IEnumerator<int> V_2,
                                int V_3)
                  IL_0000:  ldloca.s   V_1
                  IL_0002:  initobj    "S<int>"
                  IL_0008:  ldloca.s   V_1
                  IL_000a:  ldc.i4.1
                  IL_000b:  call       "void S<int>.Add(int)"
                  IL_0010:  ldloca.s   V_1
                  IL_0012:  ldc.i4.2
                  IL_0013:  call       "void S<int>.Add(int)"
                  IL_0018:  ldloc.1
                  IL_0019:  stloc.0
                  IL_001a:  ldloca.s   V_1
                  IL_001c:  initobj    "S<int>"
                  IL_0022:  ldarga.s   V_0
                  IL_0024:  call       "System.Collections.Generic.IEnumerator<int> S<int>.GetEnumerator()"
                  IL_0029:  stloc.2
                  .try
                  {
                    IL_002a:  br.s       IL_003b
                    IL_002c:  ldloc.2
                    IL_002d:  callvirt   "int System.Collections.Generic.IEnumerator<int>.Current.get"
                    IL_0032:  stloc.3
                    IL_0033:  ldloca.s   V_1
                    IL_0035:  ldloc.3
                    IL_0036:  call       "void S<int>.Add(int)"
                    IL_003b:  ldloc.2
                    IL_003c:  callvirt   "bool System.Collections.IEnumerator.MoveNext()"
                    IL_0041:  brtrue.s   IL_002c
                    IL_0043:  leave.s    IL_004f
                  }
                  finally
                  {
                    IL_0045:  ldloc.2
                    IL_0046:  brfalse.s  IL_004e
                    IL_0048:  ldloc.2
                    IL_0049:  callvirt   "void System.IDisposable.Dispose()"
                    IL_004e:  endfinally
                  }
                  IL_004f:  ldloca.s   V_0
                  IL_0051:  call       "System.Collections.Generic.IEnumerator<int> S<int>.GetEnumerator()"
                  IL_0056:  stloc.2
                  .try
                  {
                    IL_0057:  br.s       IL_0068
                    IL_0059:  ldloc.2
                    IL_005a:  callvirt   "int System.Collections.Generic.IEnumerator<int>.Current.get"
                    IL_005f:  stloc.3
                    IL_0060:  ldloca.s   V_1
                    IL_0062:  ldloc.3
                    IL_0063:  call       "void S<int>.Add(int)"
                    IL_0068:  ldloc.2
                    IL_0069:  callvirt   "bool System.Collections.IEnumerator.MoveNext()"
                    IL_006e:  brtrue.s   IL_0059
                    IL_0070:  leave.s    IL_007c
                  }
                  finally
                  {
                    IL_0072:  ldloc.2
                    IL_0073:  brfalse.s  IL_007b
                    IL_0075:  ldloc.2
                    IL_0076:  callvirt   "void System.IDisposable.Dispose()"
                    IL_007b:  endfinally
                  }
                  IL_007c:  ldloc.1
                  IL_007d:  ret
                }
                """);
        }

        [Fact]
        public void SpreadElement_04()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        var a = [1, 2, ..[]];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,26): error CS9176: There is no target type for the collection expression.
                //         var a = [1, 2, ..[]];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(5, 26));
        }

        [Fact]
        public void SpreadElement_05()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        int[] a = [1, 2];
                        a = [..a, ..[]];
                        a = [..[default]];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,21): error CS9176: There is no target type for the collection expression.
                //         a = [..a, ..[]];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(6, 21),
                // (7,16): error CS9176: There is no target type for the collection expression.
                //         a = [..[default]];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[default]").WithLocation(7, 16));
        }

        [Fact]
        public void SpreadElement_06()
        {
            string source = """
                class Program
                {
                    static string[] Append(string a, string b, bool c)
                    {
                        return [a, b, .. c ? [null] : []];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (5,26): error CS0173: Type of conditional expression cannot be determined because there is no implicit conversion between 'collection expressions' and 'collection expressions'
                //         return [a, b, .. c ? [null] : []];
                Diagnostic(ErrorCode.ERR_InvalidQM, "c ? [null] : []").WithArguments("collection expressions", "collection expressions").WithLocation(5, 26));
        }

        [Fact]
        public void SpreadElement_07()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        int[,] a = new[,] { { 1, 2 }, { 3, 4 } };
                        int[] b = F(a);
                        b.Report();
                    }
                    static int[] F(int[,] a) => [..a];
                }
                """;
            var verifier = CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2, 3, 4], ");
            verifier.VerifyIL("Program.F",
                """
                {
                  // Code size       95 (0x5f)
                  .maxstack  3
                  .locals init (System.Collections.Generic.List<int> V_0,
                                int[,] V_1,
                                int V_2,
                                int V_3,
                                int V_4,
                                int V_5,
                                int V_6)
                  IL_0000:  newobj     "System.Collections.Generic.List<int>..ctor()"
                  IL_0005:  stloc.0
                  IL_0006:  ldarg.0
                  IL_0007:  stloc.1
                  IL_0008:  ldloc.1
                  IL_0009:  ldc.i4.0
                  IL_000a:  callvirt   "int System.Array.GetUpperBound(int)"
                  IL_000f:  stloc.2
                  IL_0010:  ldloc.1
                  IL_0011:  ldc.i4.1
                  IL_0012:  callvirt   "int System.Array.GetUpperBound(int)"
                  IL_0017:  stloc.3
                  IL_0018:  ldloc.1
                  IL_0019:  ldc.i4.0
                  IL_001a:  callvirt   "int System.Array.GetLowerBound(int)"
                  IL_001f:  stloc.s    V_4
                  IL_0021:  br.s       IL_0053
                  IL_0023:  ldloc.1
                  IL_0024:  ldc.i4.1
                  IL_0025:  callvirt   "int System.Array.GetLowerBound(int)"
                  IL_002a:  stloc.s    V_5
                  IL_002c:  br.s       IL_0048
                  IL_002e:  ldloc.1
                  IL_002f:  ldloc.s    V_4
                  IL_0031:  ldloc.s    V_5
                  IL_0033:  call       "int[*,*].Get"
                  IL_0038:  stloc.s    V_6
                  IL_003a:  ldloc.0
                  IL_003b:  ldloc.s    V_6
                  IL_003d:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                  IL_0042:  ldloc.s    V_5
                  IL_0044:  ldc.i4.1
                  IL_0045:  add
                  IL_0046:  stloc.s    V_5
                  IL_0048:  ldloc.s    V_5
                  IL_004a:  ldloc.3
                  IL_004b:  ble.s      IL_002e
                  IL_004d:  ldloc.s    V_4
                  IL_004f:  ldc.i4.1
                  IL_0050:  add
                  IL_0051:  stloc.s    V_4
                  IL_0053:  ldloc.s    V_4
                  IL_0055:  ldloc.2
                  IL_0056:  ble.s      IL_0023
                  IL_0058:  ldloc.0
                  IL_0059:  callvirt   "int[] System.Collections.Generic.List<int>.ToArray()"
                  IL_005e:  ret
                }
                """);
        }

        [Fact]
        public void SpreadElement_08()
        {
            string source = """
                class Program
                {
                    static void Main()
                    {
                        int[] a = [1, 2, 3];
                        object[] b = F1(a);
                        b.Report();
                        long?[] c = F2(a);
                        c.Report();
                        object[] d = F3<int, object>(a);
                        d.Report();
                    }
                    static object[] F1(int[] a) => [..a];
                    static long?[] F2(int[] a) => [..a];
                    static U[] F3<T, U>(T[] a) where T : U => [..a];
                }
                """;
            var verifier = CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2, 3], [1, 2, 3], [1, 2, 3], ");
            verifier.VerifyIL("Program.F1",
                """
                {
                  // Code size       45 (0x2d)
                  .maxstack  2
                  .locals init (System.Collections.Generic.List<object> V_0,
                                int[] V_1,
                                int V_2,
                                int V_3)
                  IL_0000:  newobj     "System.Collections.Generic.List<object>..ctor()"
                  IL_0005:  stloc.0
                  IL_0006:  ldarg.0
                  IL_0007:  stloc.1
                  IL_0008:  ldc.i4.0
                  IL_0009:  stloc.2
                  IL_000a:  br.s       IL_0020
                  IL_000c:  ldloc.1
                  IL_000d:  ldloc.2
                  IL_000e:  ldelem.i4
                  IL_000f:  stloc.3
                  IL_0010:  ldloc.0
                  IL_0011:  ldloc.3
                  IL_0012:  box        "int"
                  IL_0017:  callvirt   "void System.Collections.Generic.List<object>.Add(object)"
                  IL_001c:  ldloc.2
                  IL_001d:  ldc.i4.1
                  IL_001e:  add
                  IL_001f:  stloc.2
                  IL_0020:  ldloc.2
                  IL_0021:  ldloc.1
                  IL_0022:  ldlen
                  IL_0023:  conv.i4
                  IL_0024:  blt.s      IL_000c
                  IL_0026:  ldloc.0
                  IL_0027:  callvirt   "object[] System.Collections.Generic.List<object>.ToArray()"
                  IL_002c:  ret
                }
                """);
            verifier.VerifyIL("Program.F2",
                """
                {
                  // Code size       46 (0x2e)
                  .maxstack  2
                  .locals init (System.Collections.Generic.List<long?> V_0,
                                int[] V_1,
                                int V_2,
                                int V_3)
                  IL_0000:  newobj     "System.Collections.Generic.List<long?>..ctor()"
                  IL_0005:  stloc.0
                  IL_0006:  ldarg.0
                  IL_0007:  stloc.1
                  IL_0008:  ldc.i4.0
                  IL_0009:  stloc.2
                  IL_000a:  br.s       IL_0021
                  IL_000c:  ldloc.1
                  IL_000d:  ldloc.2
                  IL_000e:  ldelem.i4
                  IL_000f:  stloc.3
                  IL_0010:  ldloc.0
                  IL_0011:  ldloc.3
                  IL_0012:  conv.i8
                  IL_0013:  newobj     "long?..ctor(long)"
                  IL_0018:  callvirt   "void System.Collections.Generic.List<long?>.Add(long?)"
                  IL_001d:  ldloc.2
                  IL_001e:  ldc.i4.1
                  IL_001f:  add
                  IL_0020:  stloc.2
                  IL_0021:  ldloc.2
                  IL_0022:  ldloc.1
                  IL_0023:  ldlen
                  IL_0024:  conv.i4
                  IL_0025:  blt.s      IL_000c
                  IL_0027:  ldloc.0
                  IL_0028:  callvirt   "long?[] System.Collections.Generic.List<long?>.ToArray()"
                  IL_002d:  ret
                }
                """);
            verifier.VerifyIL("Program.F3<T, U>",
                """
                {
                  // Code size       54 (0x36)
                  .maxstack  2
                  .locals init (System.Collections.Generic.List<U> V_0,
                                T[] V_1,
                                int V_2,
                                T V_3)
                  IL_0000:  newobj     "System.Collections.Generic.List<U>..ctor()"
                  IL_0005:  stloc.0
                  IL_0006:  ldarg.0
                  IL_0007:  stloc.1
                  IL_0008:  ldc.i4.0
                  IL_0009:  stloc.2
                  IL_000a:  br.s       IL_0029
                  IL_000c:  ldloc.1
                  IL_000d:  ldloc.2
                  IL_000e:  ldelem     "T"
                  IL_0013:  stloc.3
                  IL_0014:  ldloc.0
                  IL_0015:  ldloc.3
                  IL_0016:  box        "T"
                  IL_001b:  unbox.any  "U"
                  IL_0020:  callvirt   "void System.Collections.Generic.List<U>.Add(U)"
                  IL_0025:  ldloc.2
                  IL_0026:  ldc.i4.1
                  IL_0027:  add
                  IL_0028:  stloc.2
                  IL_0029:  ldloc.2
                  IL_002a:  ldloc.1
                  IL_002b:  ldlen
                  IL_002c:  conv.i4
                  IL_002d:  blt.s      IL_000c
                  IL_002f:  ldloc.0
                  IL_0030:  callvirt   "U[] System.Collections.Generic.List<U>.ToArray()"
                  IL_0035:  ret
                }
                """);
        }

        [Theory]
        [InlineData("List")]
        [InlineData("Span")]
        [InlineData("ReadOnlySpan")]
        public void SpreadElement_09(string collectionType)
        {
            string source = $$"""
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        {{collectionType}}<int> a = [1, 2, 3];
                        F1(a);
                        F2<int, object>(a);
                    }
                    static void F1({{collectionType}}<int> a)
                    {
                        {{collectionType}}<object> b = [..a];
                        b.Report();
                    }
                    static void F2<T, U>({{collectionType}}<T> a) where T : U
                    {
                        {{collectionType}}<U> b = [..a];
                        b.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net70,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], [1, 2, 3], "));
        }

        [Fact]
        public void SpreadElement_10()
        {
            string source = """
                using System.Collections;
                class Program
                {
                    static void Main()
                    {
                        IEnumerable a = new[] { 1, 2, 3 };
                        object[] b = [..a, 4];
                        b.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2, 3, 4], ");
        }

        [Fact]
        public void SpreadElement_11()
        {
            string source = """
                using System.Collections;
                class Program
                {
                    static void Main()
                    {
                        F([1, 2, 3]);
                    }
                    static int[] F(IEnumerable s) => [..s];
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,11): error CS1503: Argument 1: cannot convert from 'collection expressions' to 'System.Collections.IEnumerable'
                //         F([1, 2, 3]);
                Diagnostic(ErrorCode.ERR_BadArgType, "[1, 2, 3]").WithArguments("1", "collection expressions", "System.Collections.IEnumerable").WithLocation(6, 11),
                // (8,39): error CS1950: The best overloaded Add method 'List<int>.Add(int)' for the collection initializer has some invalid arguments
                //     static int[] F(IEnumerable s) => [..s];
                Diagnostic(ErrorCode.ERR_BadArgTypesForCollectionAdd, "..s").WithArguments("System.Collections.Generic.List<int>.Add(int)").WithLocation(8, 39),
                // (8,39): error CS1503: Argument 1: cannot convert from 'object' to 'int'
                //     static int[] F(IEnumerable s) => [..s];
                Diagnostic(ErrorCode.ERR_BadArgType, "..s").WithArguments("1", "object", "int").WithLocation(8, 39));
        }

        [Theory]
        [InlineData("object[]")]
        [InlineData("List<object>")]
        [InlineData("int[]")]
        [InlineData("List<int>")]
        public void SpreadElement_Dynamic_01(string resultType)
        {
            string source = $$"""
                using System.Collections.Generic;
                class Program
                {
                    static {{resultType}} F(List<dynamic> e)
                    {
                        return [..e];
                    }
                    static void Main()
                    {
                        var a = F([1, 2, 3]);
                        a.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(new[] { source, s_collectionExtensions }, references: new[] { CSharpRef }, options: TestOptions.ReleaseExe, expectedOutput: "[1, 2, 3], ");
            if (resultType == "List<object>")
            {
                verifier.VerifyIL("Program.F",
                    """
                    {
                      // Code size      141 (0x8d)
                      .maxstack  9
                      .locals init (System.Collections.Generic.List<object> V_0,
                                    System.Collections.Generic.List<dynamic>.Enumerator V_1,
                                    object V_2)
                      IL_0000:  newobj     "System.Collections.Generic.List<object>..ctor()"
                      IL_0005:  stloc.0
                      IL_0006:  ldarg.0
                      IL_0007:  callvirt   "System.Collections.Generic.List<dynamic>.Enumerator System.Collections.Generic.List<dynamic>.GetEnumerator()"
                      IL_000c:  stloc.1
                      .try
                      {
                        IL_000d:  br.s       IL_0072
                        IL_000f:  ldloca.s   V_1
                        IL_0011:  call       "dynamic System.Collections.Generic.List<dynamic>.Enumerator.Current.get"
                        IL_0016:  stloc.2
                        IL_0017:  ldsfld     "System.Runtime.CompilerServices.CallSite<System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>> Program.<>o__0.<>p__0"
                        IL_001c:  brtrue.s   IL_005c
                        IL_001e:  ldc.i4     0x100
                        IL_0023:  ldstr      "Add"
                        IL_0028:  ldnull
                        IL_0029:  ldtoken    "Program"
                        IL_002e:  call       "System.Type System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)"
                        IL_0033:  ldc.i4.2
                        IL_0034:  newarr     "Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo"
                        IL_0039:  dup
                        IL_003a:  ldc.i4.0
                        IL_003b:  ldc.i4.1
                        IL_003c:  ldnull
                        IL_003d:  call       "Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags, string)"
                        IL_0042:  stelem.ref
                        IL_0043:  dup
                        IL_0044:  ldc.i4.1
                        IL_0045:  ldc.i4.0
                        IL_0046:  ldnull
                        IL_0047:  call       "Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags, string)"
                        IL_004c:  stelem.ref
                        IL_004d:  call       "System.Runtime.CompilerServices.CallSiteBinder Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember(Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags, string, System.Collections.Generic.IEnumerable<System.Type>, System.Type, System.Collections.Generic.IEnumerable<Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo>)"
                        IL_0052:  call       "System.Runtime.CompilerServices.CallSite<System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>> System.Runtime.CompilerServices.CallSite<System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>>.Create(System.Runtime.CompilerServices.CallSiteBinder)"
                        IL_0057:  stsfld     "System.Runtime.CompilerServices.CallSite<System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>> Program.<>o__0.<>p__0"
                        IL_005c:  ldsfld     "System.Runtime.CompilerServices.CallSite<System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>> Program.<>o__0.<>p__0"
                        IL_0061:  ldfld      "System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic> System.Runtime.CompilerServices.CallSite<System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>>.Target"
                        IL_0066:  ldsfld     "System.Runtime.CompilerServices.CallSite<System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>> Program.<>o__0.<>p__0"
                        IL_006b:  ldloc.0
                        IL_006c:  ldloc.2
                        IL_006d:  callvirt   "void System.Action<System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic>.Invoke(System.Runtime.CompilerServices.CallSite, System.Collections.Generic.List<object>, dynamic)"
                        IL_0072:  ldloca.s   V_1
                        IL_0074:  call       "bool System.Collections.Generic.List<dynamic>.Enumerator.MoveNext()"
                        IL_0079:  brtrue.s   IL_000f
                        IL_007b:  leave.s    IL_008b
                      }
                      finally
                      {
                        IL_007d:  ldloca.s   V_1
                        IL_007f:  constrained. "System.Collections.Generic.List<dynamic>.Enumerator"
                        IL_0085:  callvirt   "void System.IDisposable.Dispose()"
                        IL_008a:  endfinally
                      }
                      IL_008b:  ldloc.0
                      IL_008c:  ret
                    }
                    """);
            }
        }

        [Fact]
        public void SpreadElement_MissingList()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        int[] a = [1, 2];
                        IEnumerable<int> e = a;
                        int[] b;
                        b = [..a];
                        b = [..e];
                    }
                }
                """;

            var comp = CreateCompilation(source);
            comp.MakeTypeMissing(WellKnownType.System_Collections_Generic_List_T);
            // https://github.com/dotnet/roslyn/issues/68785: Should not report missing List<T> for [..a].
            comp.VerifyEmitDiagnostics(
                // (9,13): error CS0656: Missing compiler required member 'System.Collections.Generic.List`1.ToArray'
                //         b = [..a];
                Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "[..a]").WithArguments("System.Collections.Generic.List`1", "ToArray").WithLocation(9, 13),
                // (9,13): error CS0518: Predefined type 'System.Collections.Generic.List`1' is not defined or imported
                //         b = [..a];
                Diagnostic(ErrorCode.ERR_PredefinedTypeNotFound, "[..a]").WithArguments("System.Collections.Generic.List`1").WithLocation(9, 13),
                // (10,13): error CS0656: Missing compiler required member 'System.Collections.Generic.List`1.ToArray'
                //         b = [..e];
                Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "[..e]").WithArguments("System.Collections.Generic.List`1", "ToArray").WithLocation(10, 13),
                // (10,13): error CS0518: Predefined type 'System.Collections.Generic.List`1' is not defined or imported
                //         b = [..e];
                Diagnostic(ErrorCode.ERR_PredefinedTypeNotFound, "[..e]").WithArguments("System.Collections.Generic.List`1").WithLocation(10, 13));

            comp = CreateCompilation(source);
            comp.MakeMemberMissing(WellKnownMember.System_Collections_Generic_List_T__ToArray);
            // https://github.com/dotnet/roslyn/issues/68785: Should not report missing List<T>.ToArray() for [..a].
            comp.VerifyEmitDiagnostics(
                // (9,13): error CS0656: Missing compiler required member 'System.Collections.Generic.List`1.ToArray'
                //         b = [..a];
                Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "[..a]").WithArguments("System.Collections.Generic.List`1", "ToArray").WithLocation(9, 13),
                // (10,13): error CS0656: Missing compiler required member 'System.Collections.Generic.List`1.ToArray'
                //         b = [..e];
                Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "[..e]").WithArguments("System.Collections.Generic.List`1", "ToArray").WithLocation(10, 13));
        }

        [CombinatorialData]
        [Theory]
        public void ArrayEmpty_01([CombinatorialValues(TargetFramework.Mscorlib45Extended, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            if (!ExecutionConditionUtil.IsCoreClr && targetFramework == TargetFramework.Net80) return;

            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        EmptyArray<object>().Report();
                        EmptyIEnumerable<object>().Report();
                        EmptyICollection<object>().Report();
                        EmptyIList<object>().Report();
                        EmptyIReadOnlyCollection<object>().Report();
                        EmptyIReadOnlyList<object>().Report();
                    }
                    static T[] EmptyArray<T>() => [];
                    static IEnumerable<T> EmptyIEnumerable<T>() => [];
                    static ICollection<T> EmptyICollection<T>() => [];
                    static IList<T> EmptyIList<T>() => [];
                    static IReadOnlyCollection<T> EmptyIReadOnlyCollection<T>() => [];
                    static IReadOnlyList<T> EmptyIReadOnlyList<T>() => [];
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                targetFramework: targetFramework,
                expectedOutput: "[], [], [], [], [], [], ");

            string expectedIL = (targetFramework == TargetFramework.Mscorlib45Extended) ?
                """
                {
                  // Code size        7 (0x7)
                  .maxstack  1
                  IL_0000:  ldc.i4.0
                  IL_0001:  newarr     "T"
                  IL_0006:  ret
                }
                """ :
                """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  call       "T[] System.Array.Empty<T>()"
                  IL_0005:  ret
                }
                """;
            verifier.VerifyIL("Program.EmptyArray<T>", expectedIL);
            verifier.VerifyIL("Program.EmptyIEnumerable<T>", expectedIL);
            verifier.VerifyIL("Program.EmptyIReadOnlyCollection<T>", expectedIL);
            verifier.VerifyIL("Program.EmptyIReadOnlyList<T>", expectedIL);

            expectedIL =
                """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  newobj     "System.Collections.Generic.List<T>..ctor()"
                  IL_0005:  ret
                }
                """;
            verifier.VerifyIL("Program.EmptyICollection<T>", expectedIL);
            verifier.VerifyIL("Program.EmptyIList<T>", expectedIL);
        }

        [CombinatorialData]
        [Theory]
        public void ArrayEmpty_02([CombinatorialValues(TargetFramework.Mscorlib45Extended, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            if (!ExecutionConditionUtil.IsCoreClr && targetFramework == TargetFramework.Net80) return;

            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        EmptyArray().Report();
                        EmptyIEnumerable().Report();
                        EmptyICollection().Report();
                        EmptyIList().Report();
                        EmptyIReadOnlyCollection().Report();
                        EmptyIReadOnlyList().Report();
                    }
                    static string[] EmptyArray() => [];
                    static IEnumerable<string> EmptyIEnumerable() => [];
                    static ICollection<string> EmptyICollection() => [];
                    static IList<string> EmptyIList() => [];
                    static IReadOnlyCollection<string> EmptyIReadOnlyCollection() => [];
                    static IReadOnlyList<string> EmptyIReadOnlyList() => [];
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                targetFramework: targetFramework,
                expectedOutput: "[], [], [], [], [], [], ");

            string expectedIL = (targetFramework == TargetFramework.Mscorlib45Extended) ?
                """
                {
                  // Code size        7 (0x7)
                  .maxstack  1
                  IL_0000:  ldc.i4.0
                  IL_0001:  newarr     "string"
                  IL_0006:  ret
                }
                """ :
                """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  call       "string[] System.Array.Empty<string>()"
                  IL_0005:  ret
                }
                """;
            verifier.VerifyIL("Program.EmptyArray", expectedIL);
            verifier.VerifyIL("Program.EmptyIEnumerable", expectedIL);
            verifier.VerifyIL("Program.EmptyIReadOnlyCollection", expectedIL);
            verifier.VerifyIL("Program.EmptyIReadOnlyList", expectedIL);

            expectedIL =
                """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  newobj     "System.Collections.Generic.List<string>..ctor()"
                  IL_0005:  ret
                }
                """;
            verifier.VerifyIL("Program.EmptyICollection", expectedIL);
            verifier.VerifyIL("Program.EmptyIList", expectedIL);
        }

        [Fact]
        public void ArrayEmpty_PointerElementType()
        {
            string source = """
                unsafe class Program
                {
                    static void Main()
                    {
                        EmptyArray().Report();
                        EmptyNestedArray().Report();
                    }
                    static void*[] EmptyArray() => [];
                    static void*[][] EmptyNestedArray() => [];
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                options: TestOptions.UnsafeReleaseExe,
                verify: Verification.FailsPEVerify,
                expectedOutput: "[], [], ");
            verifier.VerifyIL("Program.EmptyArray",
                """
                {
                  // Code size        7 (0x7)
                  .maxstack  1
                  IL_0000:  ldc.i4.0
                  IL_0001:  newarr     "void*"
                  IL_0006:  ret
                }
                """);
            verifier.VerifyIL("Program.EmptyNestedArray",
                """
                {
                  // Code size        6 (0x6)
                  .maxstack  1
                  IL_0000:  call       "void*[][] System.Array.Empty<void*[]>()"
                  IL_0005:  ret
                }
                """);
        }

        [Fact]
        public void ArrayEmpty_MissingMethod()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        int[] x = [];
                        IEnumerable<int> y = [];
                        x.Report();
                        y.Report();
                    }
                }
                """;

            var comp = CreateCompilation(new[] { source, s_collectionExtensions }, options: TestOptions.ReleaseExe);
            var verifier = CompileAndVerify(comp, expectedOutput: "[], [], ");
            verifier.VerifyIL("Program.Main",
                """
                {
                  // Code size       25 (0x19)
                  .maxstack  2
                  .locals init (System.Collections.Generic.IEnumerable<int> V_0) //y
                  IL_0000:  call       "int[] System.Array.Empty<int>()"
                  IL_0005:  call       "int[] System.Array.Empty<int>()"
                  IL_000a:  stloc.0
                  IL_000b:  ldc.i4.0
                  IL_000c:  call       "void CollectionExtensions.Report(object, bool)"
                  IL_0011:  ldloc.0
                  IL_0012:  ldc.i4.0
                  IL_0013:  call       "void CollectionExtensions.Report(object, bool)"
                  IL_0018:  ret
                }
                """);

            comp = CreateCompilation(new[] { source, s_collectionExtensions }, options: TestOptions.ReleaseExe);
            comp.MakeMemberMissing(WellKnownMember.System_Array__Empty);
            verifier = CompileAndVerify(comp, expectedOutput: "[], [], ");
            verifier.VerifyIL("Program.Main",
                """
                {
                  // Code size       27 (0x1b)
                  .maxstack  3
                  .locals init (int[] V_0) //x
                  IL_0000:  ldc.i4.0
                  IL_0001:  newarr     "int"
                  IL_0006:  stloc.0
                  IL_0007:  ldc.i4.0
                  IL_0008:  newarr     "int"
                  IL_000d:  ldloc.0
                  IL_000e:  ldc.i4.0
                  IL_000f:  call       "void CollectionExtensions.Report(object, bool)"
                  IL_0014:  ldc.i4.0
                  IL_0015:  call       "void CollectionExtensions.Report(object, bool)"
                  IL_001a:  ret
                }
                """);
        }

        [Fact]
        public void Nullable_01()
        {
            string source = """
                #nullable enable
                class Program
                {
                    static void Main()
                    {
                        object?[] x = [1];
                        x[0].ToString(); // 1
                        object[] y = [null]; // 2
                        y[0].ToString();
                        y = [2, null]; // 3
                        y[1].ToString();
                        object[]? z = [];
                        z.ToString();
                        z = [3];
                        z.ToString();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            // https://github.com/dotnet/roslyn/issues/68786: // 2 should be reported as a warning (compare with array initializer: new object[] { null }).
            comp.VerifyEmitDiagnostics(
                // (7,9): warning CS8602: Dereference of a possibly null reference.
                //         x[0].ToString(); // 1
                Diagnostic(ErrorCode.WRN_NullReferenceReceiver, "x[0]").WithLocation(7, 9));
        }

        [Fact]
        public void Nullable_02()
        {
            string source = """
                #nullable enable
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        List<object?> x = [1];
                        x[0].ToString(); // 1
                        List<object> y = [null]; // 2
                        y[0].ToString();
                        y = [2, null]; // 3
                        y[1].ToString();
                        List<object>? z = [];
                        z.ToString();
                        z = [3];
                        z.ToString();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (8,9): warning CS8602: Dereference of a possibly null reference.
                //         x[0].ToString(); // 1
                Diagnostic(ErrorCode.WRN_NullReferenceReceiver, "x[0]").WithLocation(8, 9),
                // (9,27): warning CS8625: Cannot convert null literal to non-nullable reference type.
                //         List<object> y = [null]; // 2
                Diagnostic(ErrorCode.WRN_NullAsNonNullable, "null").WithLocation(9, 27),
                // (11,17): warning CS8625: Cannot convert null literal to non-nullable reference type.
                //         y = [2, null]; // 3
                Diagnostic(ErrorCode.WRN_NullAsNonNullable, "null").WithLocation(11, 17));
        }

        [Fact]
        public void Nullable_03()
        {
            string source = """
                #nullable enable
                using System.Collections;
                struct S<T> : IEnumerable
                {
                    public void Add(T t) { }
                    public T this[int index] => default!;
                    IEnumerator IEnumerable.GetEnumerator() => default!;
                }
                class Program
                {
                    static void Main()
                    {
                        S<object?> x = [1];
                        x[0].ToString(); // 1
                        S<object> y = [null]; // 2
                        y[0].ToString();
                        y = [2, null]; // 3
                        y[1].ToString();
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (14,9): warning CS8602: Dereference of a possibly null reference.
                //         x[0].ToString(); // 1
                Diagnostic(ErrorCode.WRN_NullReferenceReceiver, "x[0]").WithLocation(14, 9),
                // (15,24): warning CS8625: Cannot convert null literal to non-nullable reference type.
                //         S<object> y = [null]; // 2
                Diagnostic(ErrorCode.WRN_NullAsNonNullable, "null").WithLocation(15, 24),
                // (17,17): warning CS8625: Cannot convert null literal to non-nullable reference type.
                //         y = [2, null]; // 3
                Diagnostic(ErrorCode.WRN_NullAsNonNullable, "null").WithLocation(17, 17));
        }

        [Fact]
        public void Nullable_04()
        {
            string source = """
                #nullable enable
                using System.Collections;
                struct S<T> : IEnumerable
                {
                    public void Add(T t) { }
                    public T this[int index] => default!;
                    IEnumerator IEnumerable.GetEnumerator() => default!;
                }
                class Program
                {
                    static void Main()
                    {
                        S<object>? x = [];
                        x = [];
                        S<object>? y = [1];
                        y = [2];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (13,24): error CS9174: Cannot initialize type 'S<object>?' with a collection expression because the type is not constructible.
                //         S<object>? x = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("S<object>?").WithLocation(13, 24),
                // (14,13): error CS9174: Cannot initialize type 'S<object>?' with a collection expression because the type is not constructible.
                //         x = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("S<object>?").WithLocation(14, 13),
                // (15,24): error CS9174: Cannot initialize type 'S<object>?' with a collection expression because the type is not constructible.
                //         S<object>? y = [1];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1]").WithArguments("S<object>?").WithLocation(15, 24),
                // (16,13): error CS9174: Cannot initialize type 'S<object>?' with a collection expression because the type is not constructible.
                //         y = [2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[2]").WithArguments("S<object>?").WithLocation(16, 13));
        }

        [Fact]
        public void OrderOfEvaluation()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                class C<T> : IEnumerable
                {
                    private List<T> _list = new List<T>();
                    public void Add(T t)
                    {
                        Console.WriteLine("Add {0}", t);
                        _list.Add(t);
                    }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                class Program
                {
                    static void Main()
                    {
                        C<int> x = [Get(1), Get(2)];
                        C<C<int>> y = [[Get(3)], [Get(4), Get(5)]];
                    }
                    static int Get(int value)
                    {
                        Console.WriteLine("Get {0}", value);
                        return value;
                    }
                }
                """;
            CompileAndVerify(source, expectedOutput: """
                Get 1
                Add 1
                Get 2
                Add 2
                Get 3
                Add 3
                Add C`1[System.Int32]
                Get 4
                Add 4
                Get 5
                Add 5
                Add C`1[System.Int32]
                """);
        }

        // Ensure collection expression conversions are not standard implicit conversions
        // and, as a result, are ignored when determining user-defined conversions.
        [Fact]
        public void UserDefinedConversions_01()
        {
            string source = """
                struct S
                {
                    public static implicit operator S(int[] a) => default;
                }
                class Program
                {
                    static void Main()
                    {
                        S s = [];
                        s = [1, 2];
                        s = (S)([3, 4]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (9,15): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         S s = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("S").WithLocation(9, 15),
                // (10,13): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         s = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("S").WithLocation(10, 13),
                // (11,13): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         s = (S)([3, 4]);
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "(S)([3, 4])").WithArguments("S").WithLocation(11, 13));
        }

        [Fact]
        public void UserDefinedConversions_02()
        {
            string source = """
                struct S
                {
                    public static explicit operator S(int[] a) => default;
                }
                class Program
                {
                    static void Main()
                    {
                        S s = [];
                        s = [1, 2];
                        s = (S)([3, 4]);
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (9,15): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         S s = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("S").WithLocation(9, 15),
                // (10,13): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         s = [1, 2];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[1, 2]").WithArguments("S").WithLocation(10, 13),
                // (11,13): error CS9174: Cannot initialize type 'S' with a collection expression because the type is not constructible.
                //         s = (S)([3, 4]);
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "(S)([3, 4])").WithArguments("S").WithLocation(11, 13));
        }

        [Fact]
        public void PrimaryConstructorParameters_01()
        {
            string source = """
                struct S(int x, int y, int z)
                {
                    int[] F = [x, y];
                    int[] M() => [y];
                    static void Main()
                    {
                        var s = new S(1, 2, 3);
                        s.F.Report();
                        s.M().Report();
                    }
                }
                """;

            var comp = CreateCompilation(new[] { source, s_collectionExtensions }, options: TestOptions.ReleaseExe);
            comp.VerifyEmitDiagnostics(
                // 0.cs(1,28): warning CS9113: Parameter 'z' is unread.
                // struct S(int x, int y, int z)
                Diagnostic(ErrorCode.WRN_UnreadPrimaryConstructorParameter, "z").WithArguments("z").WithLocation(1, 28));

            var verifier = CompileAndVerify(comp, expectedOutput: "[1, 2], [2], ");
            verifier.VerifyIL("S..ctor(int, int, int)",
                """
                {
                  // Code size       33 (0x21)
                  .maxstack  5
                  IL_0000:  ldarg.0
                  IL_0001:  ldarg.2
                  IL_0002:  stfld      "int S.<y>P"
                  IL_0007:  ldarg.0
                  IL_0008:  ldc.i4.2
                  IL_0009:  newarr     "int"
                  IL_000e:  dup
                  IL_000f:  ldc.i4.0
                  IL_0010:  ldarg.1
                  IL_0011:  stelem.i4
                  IL_0012:  dup
                  IL_0013:  ldc.i4.1
                  IL_0014:  ldarg.0
                  IL_0015:  ldfld      "int S.<y>P"
                  IL_001a:  stelem.i4
                  IL_001b:  stfld      "int[] S.F"
                  IL_0020:  ret
                }
                """);
        }

        [Fact]
        public void PrimaryConstructorParameters_02()
        {
            string source = """
                using System;
                class C(int x, int y, int z)
                {
                    Func<int[]> F = () => [x, y];
                    Func<int[]> M() => () => [y];
                    static void Main()
                    {
                        var c = new C(1, 2, 3);
                        c.F().Report();
                        c.M()().Report();
                    }
                }
                """;

            var comp = CreateCompilation(new[] { source, s_collectionExtensions }, options: TestOptions.ReleaseExe);
            comp.VerifyEmitDiagnostics(
                // 0.cs(2,27): warning CS9113: Parameter 'z' is unread.
                // class C(int x, int y, int z)
                Diagnostic(ErrorCode.WRN_UnreadPrimaryConstructorParameter, "z").WithArguments("z").WithLocation(2, 27));

            var verifier = CompileAndVerify(comp, verify: Verification.Fails, expectedOutput: "[1, 2], [2], ");
            verifier.VerifyIL("C..ctor(int, int, int)",
                """
                {
                  // Code size       52 (0x34)
                  .maxstack  3
                  .locals init (C.<>c__DisplayClass0_0 V_0) //CS$<>8__locals0
                  IL_0000:  ldarg.0
                  IL_0001:  ldarg.2
                  IL_0002:  stfld      "int C.<y>P"
                  IL_0007:  newobj     "C.<>c__DisplayClass0_0..ctor()"
                  IL_000c:  stloc.0
                  IL_000d:  ldloc.0
                  IL_000e:  ldarg.1
                  IL_000f:  stfld      "int C.<>c__DisplayClass0_0.x"
                  IL_0014:  ldloc.0
                  IL_0015:  ldarg.0
                  IL_0016:  stfld      "C C.<>c__DisplayClass0_0.<>4__this"
                  IL_001b:  ldarg.0
                  IL_001c:  ldloc.0
                  IL_001d:  ldftn      "int[] C.<>c__DisplayClass0_0.<.ctor>b__0()"
                  IL_0023:  newobj     "System.Func<int[]>..ctor(object, System.IntPtr)"
                  IL_0028:  stfld      "System.Func<int[]> C.F"
                  IL_002d:  ldarg.0
                  IL_002e:  call       "object..ctor()"
                  IL_0033:  ret
                }
                """);
        }

        [Fact]
        public void PrimaryConstructorParameters_03()
        {
            string source = """
                using System.Collections.Generic;
                class A(int[] x, List<int> y)
                {
                    public int[] X = x;
                    public List<int> Y = y;
                }
                class B(int x, int y, int z) : A([y, z], [z])
                {
                }
                class Program
                {
                    static void Main()
                    {
                        var b = new B(1, 2, 3);
                        b.X.Report();
                        b.Y.Report();
                    }
                }
                """;

            var comp = CreateCompilation(new[] { source, s_collectionExtensions }, options: TestOptions.ReleaseExe);
            comp.VerifyEmitDiagnostics(
                // 0.cs(7,13): warning CS9113: Parameter 'x' is unread.
                // class B(int x, int y, int z) : A([y, z], [z])
                Diagnostic(ErrorCode.WRN_UnreadPrimaryConstructorParameter, "x").WithArguments("x").WithLocation(7, 13));

            var verifier = CompileAndVerify(comp, expectedOutput: "[2, 3], [3], ");
            verifier.VerifyIL("B..ctor(int, int, int)",
                """
                {
                  // Code size       33 (0x21)
                  .maxstack  5
                  IL_0000:  ldarg.0
                  IL_0001:  ldc.i4.2
                  IL_0002:  newarr     "int"
                  IL_0007:  dup
                  IL_0008:  ldc.i4.0
                  IL_0009:  ldarg.2
                  IL_000a:  stelem.i4
                  IL_000b:  dup
                  IL_000c:  ldc.i4.1
                  IL_000d:  ldarg.3
                  IL_000e:  stelem.i4
                  IL_000f:  newobj     "System.Collections.Generic.List<int>..ctor()"
                  IL_0014:  dup
                  IL_0015:  ldarg.3
                  IL_0016:  callvirt   "void System.Collections.Generic.List<int>.Add(int)"
                  IL_001b:  call       "A..ctor(int[], System.Collections.Generic.List<int>)"
                  IL_0020:  ret
                }
                """);
        }

        [Fact]
        public void SemanticModel()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                struct S1 : IEnumerable
                {
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                struct S2
                {
                }
                class Program
                {
                    static void Main()
                    {
                        int[] v1 = [];
                        List<object> v2 = [];
                        Span<int> v3 = [];
                        ReadOnlySpan<object> v4 = [];
                        S1 v5 = [];
                        S2 v6 = [];
                        var v7 = (int[])[];
                        var v8 = (List<object>)[];
                        var v9 = (Span<int>)[];
                        var v10 = (ReadOnlySpan<object>)[];
                        var v11 = (S1)([]);
                        var v12 = (S2)([]);
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net70);
            comp.VerifyEmitDiagnostics(
                // (20,17): error CS9174: Cannot initialize type 'S2' with a collection expression because the type is not constructible.
                //         S2 v6 = [];
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("S2").WithLocation(20, 17),
                // (26,19): error CS9174: Cannot initialize type 'S2' with a collection expression because the type is not constructible.
                //         var v12 = (S2)([]);
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "(S2)([])").WithArguments("S2").WithLocation(26, 19));

            var tree = comp.SyntaxTrees[0];
            var model = comp.GetSemanticModel(tree);
            var collections = tree.GetRoot().DescendantNodes().OfType<CollectionExpressionSyntax>().ToArray();
            Assert.Equal(12, collections.Length);
            VerifyTypes(model, collections[0], expectedType: null, expectedConvertedType: "System.Int32[]", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[1], expectedType: null, expectedConvertedType: "System.Collections.Generic.List<System.Object>", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[2], expectedType: null, expectedConvertedType: "System.Span<System.Int32>", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[3], expectedType: null, expectedConvertedType: "System.ReadOnlySpan<System.Object>", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[4], expectedType: null, expectedConvertedType: "S1", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[5], expectedType: null, expectedConvertedType: "S2", ConversionKind.NoConversion);
            VerifyTypes(model, collections[6], expectedType: null, expectedConvertedType: "System.Int32[]", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[7], expectedType: null, expectedConvertedType: "System.Collections.Generic.List<System.Object>", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[8], expectedType: null, expectedConvertedType: "System.Span<System.Int32>", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[9], expectedType: null, expectedConvertedType: "System.ReadOnlySpan<System.Object>", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[10], expectedType: null, expectedConvertedType: "S1", ConversionKind.CollectionExpression);
            VerifyTypes(model, collections[11], expectedType: null, expectedConvertedType: "S2", ConversionKind.NoConversion);
        }

        private static void VerifyTypes(SemanticModel model, ExpressionSyntax expr, string expectedType, string expectedConvertedType, ConversionKind expectedConversionKind)
        {
            var typeInfo = model.GetTypeInfo(expr);
            var conversion = model.GetConversion(expr);
            Assert.Equal(expectedType, typeInfo.Type?.ToTestDisplayString());
            Assert.Equal(expectedConvertedType, typeInfo.ConvertedType?.ToTestDisplayString());
            Assert.Equal(expectedConversionKind, conversion.Kind);
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                    {
                        return new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB1 = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = F0();
                        x.Report();
                        MyCollection<int> y = F1();
                        y.Report();
                        MyCollection<object> z = F2(3, 4);
                        z.Report();
                    }
                    static MyCollection<string> F0()
                    {
                        return [];
                    }
                    static MyCollection<int> F1()
                    {
                        return [0, 1, 2];
                    }
                    static MyCollection<object> F2(int x, object y)
                    {
                        return [x, y, null];
                    }
                }
                """;

            var verifier = CompileAndVerify(
                new[] { sourceB1, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[], [0, 1, 2], [3, 4, null], "));
            verifier.VerifyIL("Program.F0",
                """
                {
                  // Code size       16 (0x10)
                  .maxstack  1
                  IL_0000:  call       "string[] System.Array.Empty<string>()"
                  IL_0005:  newobj     "System.ReadOnlySpan<string>..ctor(string[])"
                  IL_000a:  call       "MyCollection<string> MyCollectionBuilder.Create<string>(System.ReadOnlySpan<string>)"
                  IL_000f:  ret
                }
                """);
            verifier.VerifyIL("Program.F1",
                """
                {
                  // Code size       16 (0x10)
                  .maxstack  1
                  IL_0000:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12_Align=4 <PrivateImplementationDetails>.AD5DC1478DE06A4C2728EA528BD9361A4B945E92A414BF4D180CEDAAEAA5F4CC4"
                  IL_0005:  call       "System.ReadOnlySpan<int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<int>(System.RuntimeFieldHandle)"
                  IL_000a:  call       "MyCollection<int> MyCollectionBuilder.Create<int>(System.ReadOnlySpan<int>)"
                  IL_000f:  ret
                }
                """);
            verifier.VerifyIL("Program.F2",
                """
                {
                  // Code size       57 (0x39)
                  .maxstack  2
                  .locals init (<>y__InlineArray3<object> V_0)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "<>y__InlineArray3<object>"
                  IL_0008:  ldloca.s   V_0
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_0010:  ldarg.0
                  IL_0011:  box        "int"
                  IL_0016:  stind.ref
                  IL_0017:  ldloca.s   V_0
                  IL_0019:  ldc.i4.1
                  IL_001a:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_001f:  ldarg.1
                  IL_0020:  stind.ref
                  IL_0021:  ldloca.s   V_0
                  IL_0023:  ldc.i4.2
                  IL_0024:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_0029:  ldnull
                  IL_002a:  stind.ref
                  IL_002b:  ldloca.s   V_0
                  IL_002d:  ldc.i4.3
                  IL_002e:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<object>, object>(in <>y__InlineArray3<object>, int)"
                  IL_0033:  call       "MyCollection<object> MyCollectionBuilder.Create<object>(System.ReadOnlySpan<object>)"
                  IL_0038:  ret
                }
                """);

            string sourceB2 = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<object> c = F2([1, 2]);
                        c.Report();
                    }
                    static MyCollection<object> F2(MyCollection<object> c)
                    {
                        return [..c, 3];
                    }
                }
                """;

            verifier = CompileAndVerify(
                new[] { sourceB2, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("Program.F2",
                """
                {
                  // Code size       79 (0x4f)
                  .maxstack  2
                  .locals init (System.Collections.Generic.List<object> V_0,
                                System.Collections.Generic.IEnumerator<object> V_1,
                                object V_2)
                  IL_0000:  newobj     "System.Collections.Generic.List<object>..ctor()"
                  IL_0005:  stloc.0
                  IL_0006:  ldarga.s   V_0
                  IL_0008:  call       "System.Collections.Generic.IEnumerator<object> MyCollection<object>.GetEnumerator()"
                  IL_000d:  stloc.1
                  .try
                  {
                    IL_000e:  br.s       IL_001e
                    IL_0010:  ldloc.1
                    IL_0011:  callvirt   "object System.Collections.Generic.IEnumerator<object>.Current.get"
                    IL_0016:  stloc.2
                    IL_0017:  ldloc.0
                    IL_0018:  ldloc.2
                    IL_0019:  callvirt   "void System.Collections.Generic.List<object>.Add(object)"
                    IL_001e:  ldloc.1
                    IL_001f:  callvirt   "bool System.Collections.IEnumerator.MoveNext()"
                    IL_0024:  brtrue.s   IL_0010
                    IL_0026:  leave.s    IL_0032
                  }
                  finally
                  {
                    IL_0028:  ldloc.1
                    IL_0029:  brfalse.s  IL_0031
                    IL_002b:  ldloc.1
                    IL_002c:  callvirt   "void System.IDisposable.Dispose()"
                    IL_0031:  endfinally
                  }
                  IL_0032:  ldloc.0
                  IL_0033:  ldc.i4.3
                  IL_0034:  box        "int"
                  IL_0039:  callvirt   "void System.Collections.Generic.List<object>.Add(object)"
                  IL_003e:  ldloc.0
                  IL_003f:  callvirt   "object[] System.Collections.Generic.List<object>.ToArray()"
                  IL_0044:  newobj     "System.ReadOnlySpan<object>..ctor(object[])"
                  IL_0049:  call       "MyCollection<object> MyCollectionBuilder.Create<object>(System.ReadOnlySpan<object>)"
                  IL_004e:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_02A(
            [CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework,
            bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                    {
                        return new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                }
                """;
            var sources = targetFramework == TargetFramework.Net70
                ? new[] { sourceA, CollectionBuilderAttributeDefinition }
                : new[] { sourceA };
            var comp = CreateCompilation(sources, targetFramework: targetFramework);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        var x = F();
                        x.Report();
                    }
                    static MyCollection<int?> F()
                    {
                        return [1, 2, null];
                    }
                }
                """;
            comp = CreateCompilation(new[] { sourceB, s_collectionExtensions }, references: new[] { refA }, targetFramework: targetFramework, options: TestOptions.ReleaseExe);
            comp.VerifyEmitDiagnostics();

            var verifier = CompileAndVerify(
                comp,
                symbolValidator: module =>
                {
                    var type = module.GlobalNamespace.GetTypeMembers("<>y__InlineArray3").SingleOrDefault();
                    if (targetFramework == TargetFramework.Net80)
                    {
                        Assert.NotNull(type);
                    }
                    else
                    {
                        Assert.Null(type);
                    }
                },
                verify: targetFramework == TargetFramework.Net80 ? Verification.Fails : Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[1, 2, null], "));
            if (targetFramework == TargetFramework.Net80)
            {
                verifier.VerifyIL("Program.F",
                    """
                    {
                      // Code size       74 (0x4a)
                      .maxstack  2
                      .locals init (<>y__InlineArray3<int?> V_0)
                      IL_0000:  ldloca.s   V_0
                      IL_0002:  initobj    "<>y__InlineArray3<int?>"
                      IL_0008:  ldloca.s   V_0
                      IL_000a:  ldc.i4.0
                      IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray3<int?>, int?>(ref <>y__InlineArray3<int?>, int)"
                      IL_0010:  ldc.i4.1
                      IL_0011:  newobj     "int?..ctor(int)"
                      IL_0016:  stobj      "int?"
                      IL_001b:  ldloca.s   V_0
                      IL_001d:  ldc.i4.1
                      IL_001e:  call       "InlineArrayElementRef<<>y__InlineArray3<int?>, int?>(ref <>y__InlineArray3<int?>, int)"
                      IL_0023:  ldc.i4.2
                      IL_0024:  newobj     "int?..ctor(int)"
                      IL_0029:  stobj      "int?"
                      IL_002e:  ldloca.s   V_0
                      IL_0030:  ldc.i4.2
                      IL_0031:  call       "InlineArrayElementRef<<>y__InlineArray3<int?>, int?>(ref <>y__InlineArray3<int?>, int)"
                      IL_0036:  initobj    "int?"
                      IL_003c:  ldloca.s   V_0
                      IL_003e:  ldc.i4.3
                      IL_003f:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<int?>, int?>(in <>y__InlineArray3<int?>, int)"
                      IL_0044:  call       "MyCollection<int?> MyCollectionBuilder.Create<int?>(System.ReadOnlySpan<int?>)"
                      IL_0049:  ret
                    }
                    """);
            }
            else
            {
                verifier.VerifyIL("Program.F",
                    """
                    {
                      // Code size       43 (0x2b)
                      .maxstack  4
                      IL_0000:  ldc.i4.3
                      IL_0001:  newarr     "int?"
                      IL_0006:  dup
                      IL_0007:  ldc.i4.0
                      IL_0008:  ldc.i4.1
                      IL_0009:  newobj     "int?..ctor(int)"
                      IL_000e:  stelem     "int?"
                      IL_0013:  dup
                      IL_0014:  ldc.i4.1
                      IL_0015:  ldc.i4.2
                      IL_0016:  newobj     "int?..ctor(int)"
                      IL_001b:  stelem     "int?"
                      IL_0020:  newobj     "System.ReadOnlySpan<int?>..ctor(int?[])"
                      IL_0025:  call       "MyCollection<int?> MyCollectionBuilder.Create<int?>(System.ReadOnlySpan<int?>)"
                      IL_002a:  ret
                    }
                    """);
            }
        }

        // As above, but with TargetFramework.NetFramework.
        [ConditionalFact(typeof(DesktopOnly))]
        public void CollectionBuilder_02B()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                    {
                        var list = new List<T>();
                        foreach (var i in items) list.Add(i);
                        return new MyCollection<T>(list);
                    }
                }
                """;
            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        var x = F();
                        x.Report();
                    }
                    static MyCollection<int?> F()
                    {
                        return [1, 2, null];
                    }
                }
                """;
            var comp = CreateCompilationWithSpanAndMemoryExtensions(
                new[] { sourceA, sourceB, s_collectionExtensions, CollectionBuilderAttributeDefinition },
                targetFramework: TargetFramework.NetFramework,
                options: TestOptions.ReleaseExe);
            comp.VerifyEmitDiagnostics();

            var verifier = CompileAndVerify(
                comp,
                symbolValidator: module =>
                {
                    var type = module.GlobalNamespace.GetTypeMembers("<>y__InlineArray3").SingleOrDefault();
                    Assert.Null(type);
                },
                expectedOutput: "[1, 2, null], ");
            verifier.VerifyIL("Program.F",
                """
                {
                  // Code size       43 (0x2b)
                  .maxstack  4
                  IL_0000:  ldc.i4.3
                  IL_0001:  newarr     "int?"
                  IL_0006:  dup
                  IL_0007:  ldc.i4.0
                  IL_0008:  ldc.i4.1
                  IL_0009:  newobj     "int?..ctor(int)"
                  IL_000e:  stelem     "int?"
                  IL_0013:  dup
                  IL_0014:  ldc.i4.1
                  IL_0015:  ldc.i4.2
                  IL_0016:  newobj     "int?..ctor(int)"
                  IL_001b:  stelem     "int?"
                  IL_0020:  newobj     "System.ReadOnlySpan<int?>..ctor(int?[])"
                  IL_0025:  call       "MyCollection<int?> MyCollectionBuilder.Create<int?>(System.ReadOnlySpan<int?>)"
                  IL_002a:  ret
                }
                """);
        }

        [Fact]
        public void CollectionBuilder_InlineArrayTypes()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                    {
                        return new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                }
                class A
                {
                    static void M()
                    {
                        MyCollection<object> x;
                        x = [];
                        x = [null, null];
                        x = [1, 2, 3];
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            CompileAndVerify(
                comp,
                symbolValidator: module =>
                {
                    AssertEx.Equal(new[] { "<>y__InlineArray2", "<>y__InlineArray3" }, getInlineArrayTypeNames(module));
                },
                verify: Verification.Skipped);
            var refA = comp.EmitToImageReference();

            string sourceB = """
                class B
                {
                    static void M<T>(MyCollection<T> c)
                    {
                    }
                    static void M1()
                    {
                        M<int?>([1]);
                    }
                    static void M2()
                    {
                        M([(object)4, 5, 6]);
                        M(["a"]);
                        M(["b"]);
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            CompileAndVerify(
                comp,
                symbolValidator: module =>
                {
                    AssertEx.Equal(new[] { "<>y__InlineArray1", "<>y__InlineArray3" }, getInlineArrayTypeNames(module));
                },
                verify: Verification.Skipped);

            const int n = 1025;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(i);
            }
            string sourceC = $$"""
                using System;
                using System.Linq;
                class Program
                {
                    static void Main()
                    {
                        MyCollection<object> c = [{{builder.ToString()}}];
                        Console.WriteLine(c.Count());
                    }
                }
                """;
            comp = CreateCompilation(sourceC, references: new[] { refA }, targetFramework: TargetFramework.Net80, options: TestOptions.ReleaseExe);
            CompileAndVerify(
                comp,
                symbolValidator: module =>
                {
                    AssertEx.Equal(new[] { $"<>y__InlineArray{n}" }, getInlineArrayTypeNames(module));
                },
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput($"{n}"));

            static ImmutableArray<string> getInlineArrayTypeNames(ModuleSymbol module)
            {
                return module.GlobalNamespace.GetTypeMembers().WhereAsArray(t => t.Name.StartsWith("<>y__InlineArray")).SelectAsArray(t => t.Name);
            }
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_RefStructCollection(bool useCompilationReference, bool useScoped)
        {
            string qualifier = useScoped ? "scoped " : "";
            string sourceA = $$"""
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public ref struct MyCollection<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    public T[] ToArray() => _list.ToArray();
                }
                public static class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>({{qualifier}}ReadOnlySpan<T> items)
                    {
                        return new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                using System;
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        F().Report();
                    }
                    static object[] F()
                    {
                        MyCollection<object> c = [1, 2, 3];
                        return c.ToArray();
                    }
                }
                """;

            var verifier = CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("Program.F",
                $$"""
                {
                    // Code size       75 (0x4b)
                    .maxstack  2
                    .locals init (MyCollection<object> V_0, //c
                                <>y__InlineArray3<object> V_1)
                    IL_0000:  ldloca.s   V_1
                    IL_0002:  initobj    "<>y__InlineArray3<object>"
                    IL_0008:  ldloca.s   V_1
                    IL_000a:  ldc.i4.0
                    IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                    IL_0010:  ldc.i4.1
                    IL_0011:  box        "int"
                    IL_0016:  stind.ref
                    IL_0017:  ldloca.s   V_1
                    IL_0019:  ldc.i4.1
                    IL_001a:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                    IL_001f:  ldc.i4.2
                    IL_0020:  box        "int"
                    IL_0025:  stind.ref
                    IL_0026:  ldloca.s   V_1
                    IL_0028:  ldc.i4.2
                    IL_0029:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                    IL_002e:  ldc.i4.3
                    IL_002f:  box        "int"
                    IL_0034:  stind.ref
                    IL_0035:  ldloca.s   V_1
                    IL_0037:  ldc.i4.3
                    IL_0038:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<object>, object>(in <>y__InlineArray3<object>, int)"
                    IL_003d:  call       "MyCollection<object> MyCollectionBuilder.Create<object>({{qualifier}}System.ReadOnlySpan<object>)"
                    IL_0042:  stloc.0
                    IL_0043:  ldloca.s   V_0
                    IL_0045:  call       "object[] MyCollection<object>.ToArray()"
                    IL_004a:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_NonGenericCollection(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public sealed class MyCollection : IEnumerable<object>
                {
                    private readonly List<object> _list;
                    public MyCollection(List<object> list) { _list = list; }
                    public IEnumerator<object> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollection Create(ReadOnlySpan<object> items) =>
                        new MyCollection(new List<object>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        x.Report();
                        MyCollection y = [1, 2, 3];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InterfaceCollection_ReturnInterface(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public interface IMyCollection<T> : IEnumerable<T>
                {
                }
                public sealed class MyCollectionBuilder
                {
                    public static IMyCollection<T> Create<T>(ReadOnlySpan<T> items) =>
                        new MyCollection<T>(new List<T>(items.ToArray()));
                    public sealed class MyCollection<T> : IMyCollection<T>
                    {
                        private readonly List<T> _list;
                        public MyCollection(List<T> list) { _list = list; }
                        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        IMyCollection<string> x = [];
                        x.Report(includeType: true);
                        IMyCollection<int> y = [1, 2, 3];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("(MyCollectionBuilder.MyCollection<System.String>) [], (MyCollectionBuilder.MyCollection<System.Int32>) [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InterfaceCollection_ReturnImplementation(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public interface IMyCollection<T> : IEnumerable<T>
                {
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) =>
                        new MyCollection<T>(new List<T>(items.ToArray()));
                    public sealed class MyCollection<T> : IMyCollection<T>
                    {
                        private readonly List<T> _list;
                        public MyCollection(List<T> list) { _list = list; }
                        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        IMyCollection<string> x = [];
                        IMyCollection<int> y = [1, 2, 3];
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (5,35): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'IMyCollection<T>'.
                //         IMyCollection<string> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "IMyCollection<T>").WithLocation(5, 35),
                // (6,32): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'IMyCollection<T>'.
                //         IMyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[1, 2, 3]").WithArguments("Create", "T", "IMyCollection<T>").WithLocation(6, 32));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_NestedCollectionAndBuilder(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                public class Container
                {
                    [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                    public sealed class MyCollection<T> : IEnumerable<T>
                    {
                        private readonly List<T> _list;
                        public MyCollection(List<T> list) { _list = list; }
                        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    }
                    public sealed class MyCollectionBuilder
                    {
                        public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                            => new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        Container.MyCollection<string> x = [];
                        x.Report(includeType: true);
                        Container.MyCollection<object> y = [1, 2, 3];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("(Container.MyCollection<System.String>) [], (Container.MyCollection<System.Object>) [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_NoElementType(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T>
                {
                    public MyCollection(T[] array) { }
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<object> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<string> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,34): error CS9188: 'MyCollection<object>' has a CollectionBuilderAttribute but no element type.
                //         MyCollection<object> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderNoElementType, "[]").WithArguments("MyCollection<object>").WithLocation(6, 34),
                // (7,31): error CS9188: 'MyCollection<int>' has a CollectionBuilderAttribute but no element type.
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderNoElementType, "[1, 2, 3]").WithArguments("MyCollection<int>").WithLocation(7, 31));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ElementTypeFromPattern_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T>
                {
                    private readonly T[] _array;
                    public MyCollection(T[] array) { _array = array; }
                    public MyEnumerator<T> GetEnumerator()
                        => new MyEnumerator<T>(_array);
                }
                public struct MyEnumerator<T>
                {
                    private readonly T[] _array;
                    private int _index;
                    public MyEnumerator(T[] array)
                    {
                        _array = array;
                        _index = -1;
                    }
                    public bool MoveNext()
                    {
                        if (_index < _array.Length) _index++;
                        return _index < _array.Length;
                    }
                    public T Current => _array[_index];
                }
                public struct MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                        => new MyCollection<T>(items.ToArray());
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        GetElements(x).Report();
                        MyCollection<int> y = [1, 2, 3];
                        GetElements(y).Report();
                    }
                    static IEnumerable<T> GetElements<T>(MyCollection<T> c)
                    {
                        foreach (var e in c) yield return e;
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ElementTypeFromPattern_02(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection
                {
                    private readonly object[] _array;
                    public MyCollection(object[] array) { _array = array; }
                    public MyEnumerator GetEnumerator()
                        => new MyEnumerator(_array);
                }
                public struct MyEnumerator
                {
                    private readonly object[] _array;
                    private int _index;
                    public MyEnumerator(object[] array)
                    {
                        _array = array;
                        _index = -1;
                    }
                    public bool MoveNext()
                    {
                        if (_index < _array.Length) _index++;
                        return _index < _array.Length;
                    }
                    public object Current => _array[_index];
                }
                public struct MyCollectionBuilder
                {
                    public static MyCollection Create(ReadOnlySpan<object> items)
                        => new MyCollection(items.ToArray());
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                using System.Collections;
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        GetElements(x).Report();
                        MyCollection y = [1, 2, 3];
                        GetElements(y).Report();
                    }
                    static IEnumerable GetElements(MyCollection c)
                    {
                        foreach (var e in c) yield return e;
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ObjectElementType_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection : IEnumerable
                {
                    private readonly object[] _array;
                    public MyCollection(object[] array) { _array = array; }
                    IEnumerator IEnumerable.GetEnumerator() => _array.GetEnumerator();
                }
                public struct MyCollectionBuilder
                {
                    public static MyCollection Create(ReadOnlySpan<object> items)
                        => new MyCollection(items.ToArray());
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        x.Report();
                        MyCollection y = [1, 2, 3];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ObjectElementType_02(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable
                {
                    public MyCollection(T[] array) { }
                    public IEnumerator GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<object> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<string> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<object>' and return type 'MyCollection<T>'.
                //         MyCollection<object> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "object", "MyCollection<T>").WithLocation(6, 34),
                // (7,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<object>' and return type 'MyCollection<T>'.
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[1, 2, 3]").WithArguments("Create", "object", "MyCollection<T>").WithLocation(7, 31));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ConstructedElementType(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                public sealed class E<T>
                {
                    private readonly T _t;
                    public E(T t) { _t = t; }
                    public override string ToString() => $"E({_t})";
                }
                [CollectionBuilder(typeof(Builder), "Create")]
                public sealed class C<T> : IEnumerable<E<T>>
                {
                    private readonly List<E<T>> _list;
                    public C(List<E<T>> list) { _list = list; }
                    public IEnumerator<E<T>> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public sealed class Builder
                {
                    public static C<T> Create<T>(ReadOnlySpan<E<T>> items)
                        => new C<T>(new List<E<T>>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        C<string> x = [null];
                        x.Report(includeType: true);
                        C<int> y = [new E<int>(1), default];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("(C<System.String>) [null], (C<System.Int32>) [E(1), null], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_Dictionary(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyDictionaryBuilder), "Create")]
                public class MyImmutableDictionary<K, V> : IEnumerable<KeyValuePair<K, V>>
                {
                    private readonly Dictionary<K, V> _d;
                    public MyImmutableDictionary(ReadOnlySpan<KeyValuePair<K, V>> items)
                    {
                        _d = new();
                        foreach (var (k, v) in items) _d.Add(k, v);
                    }
                    public IEnumerator<KeyValuePair<K, V>> GetEnumerator() => _d.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyDictionaryBuilder
                {
                    public static MyImmutableDictionary<K, V> Create<K, V>(ReadOnlySpan<KeyValuePair<K, V>> items)
                        => new MyImmutableDictionary<K, V>(items);
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        MyImmutableDictionary<string, object> x = [];
                        x.Report();
                        MyImmutableDictionary<string, int> y = [KeyValuePair.Create("one", 1), KeyValuePair.Create("two", 2)];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[], [[one, 1], [two, 2]], "));
        }

        [Fact]
        public void CollectionBuilder_MissingBuilderType()
        {
            string sourceA = """
                public class MyCollectionBuilder
                {
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = comp.EmitToImageReference();

            string sourceB = """
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            var refB = comp.EmitToImageReference();

            string sourceC = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceC, references: new[] { refB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_MissingBuilderMethod(bool useCompilationReference)
        {
            string sourceA = """
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_NullBuilderType()
        {
            string sourceA = """
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(null, "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(4,2): error CS9185: The CollectionBuilderAttribute builder type must be a non-generic class or struct.
                // [CollectionBuilder(null, "Create")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidType, "CollectionBuilder").WithLocation(4, 2),
                // 1.cs(6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // 1.cs(7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_NullBuilderType_FromMetadata()
        {
            // [CollectionBuilder(null, "Create")]
            string sourceA = """
                .class public sealed System.Runtime.CompilerServices.CollectionBuilderAttribute extends [mscorlib]System.Attribute
                {
                  .method public hidebysig specialname rtspecialname instance void .ctor(class [mscorlib]System.Type builderType, string methodName) cil managed { ret }
                }
                .class public sealed MyCollection`1<T>
                {
                  .custom instance void System.Runtime.CompilerServices.CollectionBuilderAttribute::.ctor(class [mscorlib]System.Type, string) = { type(nullref) string('Create') }
                  .method public hidebysig specialname rtspecialname instance void .ctor() cil managed { ret }
                  .method public instance class [mscorlib]System.Collections.Generic.IEnumerator`1<!T> GetEnumerator() { ldnull ret }
                }
                """;
            var refA = CompileIL(sourceA);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(sourceB, references: new[] { refA });
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_InvalidBuilderType_Interface()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public interface MyCollectionBuilder
                {
                    MyCollection<T> Create<T>(ReadOnlySpan<T> items);
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(5,2): error CS9185: The CollectionBuilderAttribute builder type must be a non-generic class or struct.
                // [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidType, "CollectionBuilder").WithLocation(5, 2),
                // 1.cs(6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // 1.cs(7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_InvalidBuilderType_Interface_FromMetadata()
        {
            // [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
            // class MyCollection<T> { ... }
            // interface MyCollectionBuilder { ... }
            string sourceA = """
                .class public sealed System.ReadOnlySpan`1<T> extends [mscorlib]System.ValueType
                {
                }
                .class public sealed System.Runtime.CompilerServices.CollectionBuilderAttribute extends [mscorlib]System.Attribute
                {
                  .method public hidebysig specialname rtspecialname instance void .ctor(class [mscorlib]System.Type builderType, string methodName) cil managed { ret }
                }
                .class public sealed MyCollection`1<T>
                {
                  .custom instance void System.Runtime.CompilerServices.CollectionBuilderAttribute::.ctor(class [mscorlib]System.Type, string) = { type(MyCollectionBuilder) string('Create') }
                  .method public hidebysig specialname rtspecialname instance void .ctor() cil managed { ret }
                  .method public instance class [mscorlib]System.Collections.Generic.IEnumerator`1<!T> GetEnumerator() { ldnull ret }
                }
                .class interface public abstract MyCollectionBuilder
                {
                  .method public abstract virtual instance class MyCollection`1<!!T> Create<T>(valuetype System.ReadOnlySpan`1<!!T> items) { }
                }
                """;
            var refA = CompileIL(sourceA);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(sourceB, references: new[] { refA });
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InvalidBuilderType_03(
            [CombinatorialValues("public delegate void MyCollectionBuilder();", "public enum MyCollectionBuilder { }")] string builderTypeDefinition)
        {
            string sourceA = $$"""
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "ToString")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                {{builderTypeDefinition}}
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(5,2): error CS9185: The CollectionBuilderAttribute builder type must be a non-generic class or struct.
                // [CollectionBuilder(typeof(MyCollectionBuilder), "ToString")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidType, "CollectionBuilder").WithLocation(5, 2),
                // 1.cs(6,31): error CS9187: Could not find an accessible 'ToString' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("ToString", "T", "MyCollection<T>").WithLocation(6, 31),
                // 1.cs(7,34): error CS9187: Could not find an accessible 'ToString' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("ToString", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InvalidBuilderType_04(
            [CombinatorialValues("int[]", "int*", "(object, object)")] string builderTypeName)
        {
            string sourceA = $$"""
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof({{builderTypeName}}), "ToString")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(4,2): error CS9185: The CollectionBuilderAttribute builder type must be a non-generic class or struct.
                // [CollectionBuilder(typeof(int*), "ToString")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidType, "CollectionBuilder").WithLocation(4, 2),
                // 1.cs(6,31): error CS9187: Could not find an accessible 'ToString' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("ToString", "T", "MyCollection<T>").WithLocation(6, 31),
                // 1.cs(7,34): error CS9187: Could not find an accessible 'ToString' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("ToString", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_InvalidBuilderType_TypeParameter()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                struct Container<T>
                {
                    [CollectionBuilder(typeof(T), "ToString")]
                    public struct MyCollection : IEnumerable<int>
                    {
                        IEnumerator<int> IEnumerable<int>.GetEnumerator() => default;
                        IEnumerator IEnumerable.GetEnumerator() => default;
                    }
                }
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        Container<int>.MyCollection x = [];
                        Container<string>.MyCollection y = [null];
                        Container<object>.MyCollection z = new();
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(6,24): error CS0416: 'T': an attribute argument cannot use type parameters
                //     [CollectionBuilder(typeof(T), "ToString")]
                Diagnostic(ErrorCode.ERR_AttrArgWithTypeVars, "typeof(T)").WithArguments("T").WithLocation(6, 24),
                // 0.cs(19,45): error CS1061: 'Container<string>.MyCollection' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'Container<string>.MyCollection' could be found (are you missing a using directive or an assembly reference?)
                //         Container<string>.MyCollection y = [null];
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "null").WithArguments("Container<string>.MyCollection", "Add").WithLocation(19, 45));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_NullOrEmptyMethodName([CombinatorialValues("null", "\"\"")] string methodName)
        {
            string sourceA = $$"""
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), {{methodName}})]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(4,2): error CS9186: The CollectionBuilderAttribute method name is invalid.
                // [CollectionBuilder(typeof(MyCollectionBuilder), "")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidMethodName, "CollectionBuilder").WithLocation(4, 2),
                // 1.cs(6,31): error CS9187: Could not find an accessible '' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("", "T", "MyCollection<T>").WithLocation(6, 31),
                // 1.cs(7,34): error CS9187: Could not find an accessible '' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_NullOrEmptyMethodName_FromMetadata([CombinatorialValues("nullref", "''")] string methodName)
        {
            // [CollectionBuilder(typeof(MyCollectionBuilder), "")]
            string sourceA = $$"""
                .class public sealed System.ReadOnlySpan`1<T> extends [mscorlib]System.ValueType
                {
                }
                .class public sealed System.Runtime.CompilerServices.CollectionBuilderAttribute extends [mscorlib]System.Attribute
                {
                  .method public hidebysig specialname rtspecialname instance void .ctor(class [mscorlib]System.Type builderType, string methodName) cil managed { ret }
                }
                .class public sealed MyCollection`1<T>
                {
                  .custom instance void System.Runtime.CompilerServices.CollectionBuilderAttribute::.ctor(class [mscorlib]System.Type, string) = { type(MyCollectionBuilder) string({{methodName}}) }
                  .method public hidebysig specialname rtspecialname instance void .ctor() cil managed { ret }
                  .method public instance class [mscorlib]System.Collections.Generic.IEnumerator`1<!T> GetEnumerator() { ldnull ret }
                }
                .class public sealed MyCollectionBuilder
                {
                  .method public hidebysig specialname rtspecialname instance void .ctor() cil managed { ret }
                  .method public static class MyCollection`1<!!T> Create<T>(valuetype System.ReadOnlySpan`1<!!T> items) { ldnull ret }
                }
                """;
            var refA = CompileIL(sourceA);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(sourceB, references: new[] { refA });
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible '' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible '' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InstanceMethod(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    public MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_OtherMember_01(
            [CombinatorialValues(
                "public MyCollection Create = null;",
                "public MyCollection Create => null;",
                "public class Create { }")]
            string createMember,
            bool useCompilationReference)
        {
            string sourceA = $$"""
                using System;
                using System.Collections;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public class MyCollection : IEnumerable
                {
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                        {{createMember}}
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        MyCollection y = [null];
                        MyCollection z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<object>' and return type 'MyCollection'.
                //         MyCollection x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "object", "MyCollection").WithLocation(6, 26),
                // (7,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<object>' and return type 'MyCollection'.
                //         MyCollection y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "object", "MyCollection").WithLocation(7, 26));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_TypeDifferences_Dynamic_01(bool useCompilationReference)
        {
            CollectionBuilder_TypeDifferences("object", "dynamic", "1, 2, 3", "[1, 2, 3]", useCompilationReference);
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_TypeDifferences_Dynamic_02(bool useCompilationReference)
        {
            string sourceA = $$"""
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection
                {
                    private readonly List<dynamic> _list;
                    public MyCollection(List<dynamic> list) { _list = list; }
                    public IEnumerator<dynamic> GetEnumerator() => _list.GetEnumerator();
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollection Create(ReadOnlySpan<object> items)
                        => new MyCollection(new List<dynamic>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = $$"""
                using System.Collections.Generic;
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        GetElements(x).Report();
                        MyCollection y = [1, 2, 3];
                        GetElements(y).Report();
                    }
                    static IEnumerable<object> GetElements(MyCollection c)
                    {
                        foreach (var e in c) yield return e;
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput($"[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_TypeDifferences_TupleElementNames(bool useCompilationReference)
        {
            CollectionBuilder_TypeDifferences("(int, int)", "(int A, int B)", "(1, 2), default", "[(1, 2), (0, 0)]", useCompilationReference);
            CollectionBuilder_TypeDifferences("(int A, int B)", "(int, int)", "(1, 2), default", "[(1, 2), (0, 0)]", useCompilationReference);
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_TypeDifferences_Nullability(bool useCompilationReference)
        {
            CollectionBuilder_TypeDifferences("object", "object?", "1, 2, 3", "[1, 2, 3]", useCompilationReference);
            CollectionBuilder_TypeDifferences("object?", "object", "1, null, 3", "[1, null, 3]", useCompilationReference);
        }

        private void CollectionBuilder_TypeDifferences(string collectionElementType, string builderElementType, string values, string expectedOutput, bool useCompilationReference)
        {
            string sourceA = $$"""
                #nullable enable
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection : IEnumerable<{{collectionElementType}}>
                {
                    private readonly List<{{collectionElementType}}> _list;
                    public MyCollection(List<{{collectionElementType}}> list) { _list = list; }
                    public IEnumerator<{{collectionElementType}}> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollection Create(ReadOnlySpan<{{builderElementType}}> items)
                        => new MyCollection(new List<{{collectionElementType}}>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = $$"""
                #nullable enable
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        x.Report();
                        MyCollection y = [{{values}}];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput($"[], {expectedOutput}, "));
        }

        // If there are multiple attributes, the first is used.
        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_MultipleAttributes(bool useCompilationReference)
        {
            string sourceAttribute = """
                namespace System.Runtime.CompilerServices
                {
                    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
                    public sealed class CollectionBuilderAttribute : Attribute
                    {
                        public CollectionBuilderAttribute(Type builderType, string methodName) { }
                    }
                }
                """;
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder1), "Create1")]
                [CollectionBuilder(typeof(MyCollectionBuilder2), "Create2")]
                public sealed class MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public struct MyCollectionBuilder1
                {
                    public static MyCollection<T> Create1<T>(ReadOnlySpan<T> items)
                        => new MyCollection<T>(new List<T>(items.ToArray()));
                }
                public struct MyCollectionBuilder2
                {
                    public static MyCollection<T> Create2<T>(ReadOnlySpan<T> items)
                        => throw null;
                }
                """;
            var comp = CreateCompilation(new[] { sourceAttribute, sourceA }, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static MyCollection<int> F() => [1, 2, 3];
                    static void Main()
                    {
                        F().Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            comp = (CSharpCompilation)verifier.Compilation;

            var collectionType = (NamedTypeSymbol)comp.GetMember<MethodSymbol>("Program.F").ReturnType;
            Assert.Equal("MyCollection<System.Int32>", collectionType.ToTestDisplayString());
            TypeSymbol builderType;
            string methodName;
            Assert.True(collectionType.HasCollectionBuilderAttribute(out builderType, out methodName));
            Assert.Equal("MyCollectionBuilder1", builderType.ToTestDisplayString());
            Assert.Equal("Create1", methodName);
        }

        [Fact]
        public void CollectionBuilder_GenericBuilderType_01()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder<>), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public sealed class MyCollectionBuilder<T>
                {
                    public static MyCollection<T> Create(ReadOnlySpan<T> items) => default;
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(5,2): error CS9185: The CollectionBuilderAttribute builder type must be a non-generic class or struct.
                // [CollectionBuilder(typeof(MyCollectionBuilder<>), "Create")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidType, "CollectionBuilder").WithLocation(5, 2),
                // 1.cs(6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // 1.cs(7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_GenericBuilderType_02()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder<int>), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public sealed class MyCollectionBuilder<T>
                {
                    public static MyCollection<U> Create<U>(ReadOnlySpan<U> items) => default;
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(5,2): error CS9185: The CollectionBuilderAttribute builder type must be a non-generic class or struct.
                // [CollectionBuilder(typeof(MyCollectionBuilder<int>), "Create")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidType, "CollectionBuilder").WithLocation(5, 2),
                // 1.cs(6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // 1.cs(7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_GenericBuilderType_03()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                public class Container<T>
                {
                    [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                    public struct MyCollection : IEnumerable<T>
                    {
                        private readonly List<T> _list;
                        public MyCollection(List<T> list) { _list = list; }
                        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    }
                    public sealed class MyCollectionBuilder
                    {
                        public static MyCollection Create(ReadOnlySpan<T> items)
                            => new MyCollection(new List<T>(items.ToArray()));
                    }
                }
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        Container<string>.MyCollection x = [];
                        Container<int>.MyCollection y = [default];
                        Container<object>.MyCollection z = new();
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(7,24): error CS0416: 'Container<T>.MyCollectionBuilder': an attribute argument cannot use type parameters
                //     [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                Diagnostic(ErrorCode.ERR_AttrArgWithTypeVars, "typeof(MyCollectionBuilder)").WithArguments("Container<T>.MyCollectionBuilder").WithLocation(7, 24),
                // 0.cs(27,42): error CS1061: 'Container<int>.MyCollection' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'Container<int>.MyCollection' could be found (are you missing a using directive or an assembly reference?)
                //         Container<int>.MyCollection y = [default];
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "default").WithArguments("Container<int>.MyCollection", "Add").WithLocation(27, 42));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_GenericCollectionContainerType_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                public class Container<T>
                {
                    [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                    public struct MyCollection : IEnumerable<T>
                    {
                        private readonly List<T> _list;
                        public MyCollection(List<T> list) { _list = list; }
                        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    }
                }
                public sealed class MyCollectionBuilder
                {
                    public static Container<T>.MyCollection Create<T>(ReadOnlySpan<T> items)
                        => new Container<T>.MyCollection(new List<T>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        Container<string>.MyCollection x = [];
                        x.Report(includeType: true);
                        Container<int>.MyCollection y = [1, 2, 3];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("(Container<T>.MyCollection<System.String>) [], (Container<T>.MyCollection<System.Int32>) [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_GenericCollectionContainerType_02(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                public class Container<T>
                {
                    [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                    public struct MyCollection : IEnumerable<int>
                    {
                        private readonly List<int> _list;
                        public MyCollection(List<int> list) { _list = list; }
                        public IEnumerator<int> GetEnumerator() => _list.GetEnumerator();
                        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    }
                }
                public sealed class MyCollectionBuilder
                {
                    public static Container<T>.MyCollection Create<T>(ReadOnlySpan<int> items)
                        => new Container<T>.MyCollection(new List<int>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        Container<int>.MyCollection x = [];
                        x.Report(includeType: true);
                        Container<string>.MyCollection y = [1, 2, 3];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("(Container<T>.MyCollection<System.Int32>) [], (Container<T>.MyCollection<System.String>) [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_GenericCollectionContainerType_03(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                public class Container<T>
                {
                    [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                    public struct MyCollection<U> : IEnumerable<U>
                    {
                        private readonly List<U> _list;
                        public MyCollection(List<U> list) { _list = list; }
                        public IEnumerator<U> GetEnumerator() => _list.GetEnumerator();
                        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    }
                }
                public sealed class MyCollectionBuilder
                {
                    public static Container<T>.MyCollection<U> Create<T, U>(ReadOnlySpan<U> items)
                        => new Container<T>.MyCollection<U>(new List<U>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        Container<int>.MyCollection<string> x = [];
                        x.Report(includeType: true);
                        Container<string>.MyCollection<int> y = [1, 2, 3];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("(Container<T>.MyCollection<System.Int32, System.String>) [], (Container<T>.MyCollection<System.String, System.Int32>) [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_GenericType_ElementTypeFirstOfTwo(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T, U> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollection<T, U> Create<T, U>(ReadOnlySpan<T> items)
                        => new MyCollection<T, U>(new List<T>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string, int> x = [];
                        x.Report(includeType: true);
                        MyCollection<int, string> y = [1, 2, 3];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("(MyCollection<System.String, System.Int32>) [], (MyCollection<System.Int32, System.String>) [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_GenericType_ElementTypeSecondOfTwo(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T, U> : IEnumerable<U>
                {
                    private readonly List<U> _list;
                    public MyCollection(List<U> list) { _list = list; }
                    public IEnumerator<U> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollection<T, U> Create<T, U>(ReadOnlySpan<U> items)
                        => new MyCollection<T, U>(new List<U>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int, string> x = [];
                        x.Report(includeType: true);
                        MyCollection<string, int> y = [1, 2, 3];
                        y.Report(includeType: true);
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("(MyCollection<System.Int32, System.String>) [], (MyCollection<System.String, System.Int32>) [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InaccessibleBuilderType_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                internal class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_NestedBuilderType(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public class MyCollection : IEnumerable<int>
                {
                    private readonly List<int> _list;
                    public MyCollection(List<int> list) { _list = list; }
                    public IEnumerator<int> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    public struct MyCollectionBuilder
                    {
                        public static MyCollection Create(ReadOnlySpan<int> items)
                            => new MyCollection(new List<int>(items.ToArray()));
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        x.Report();
                        MyCollection y = [1, 2, 3];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InaccessibleBuilderType_02(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public class MyCollection : IEnumerable<int>
                {
                    public IEnumerator<int> GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                    protected class MyCollectionBuilder
                    {
                        public static MyCollection Create(ReadOnlySpan<int> items) => default;
                    }
                    static readonly MyCollection _instance = [1, 2, 3];
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        MyCollection y = [1, 2, 3];
                        MyCollection z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<int>' and return type 'MyCollection'.
                //         MyCollection x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "int", "MyCollection").WithLocation(6, 26),
                // (7,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<int>' and return type 'MyCollection'.
                //         MyCollection y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[1, 2, 3]").WithArguments("Create", "int", "MyCollection").WithLocation(7, 26));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InaccessibleMethod(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    static readonly MyCollection<int> _instance = [1, 2, 3];
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    internal static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        MyCollection<string> y = [null];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 31),
                // (7,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> y = [null];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[null]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 34));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_Overloads_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>()
                    {
                        throw null;
                    }
                    public static MyCollection<T> Create<T>(Span<T> items)
                    {
                        throw null;
                    }
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                    {
                        return new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items, int index = 0)
                    {
                        throw null;
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        x.Report();
                        MyCollection<int> y = [1, 2, 3];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_Overloads_02(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                    {
                        return new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                    public static MyCollection<int> Create(ReadOnlySpan<int> items)
                    {
                        throw null;
                    }
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        x.Report();
                        MyCollection<int> y = [1, 2, 3];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_UnexpectedSignature_01(
            [CombinatorialValues(
                "public static MyCollection<int> Create(ReadOnlySpan<int> items) => default;", // constructed parameter and return types
                "public static MyCollection<T> Create<T>(ReadOnlySpan<int> items) => default;", // constructed parameter type
                "public static MyCollection<int> Create<T>(ReadOnlySpan<T> items) => default;", // constructed return type
                "public static MyCollection<T> Create<T>(ReadOnlySpan<T> items, int index = 0) => default;", // optional parameter
                "public static MyCollection<T> Create<T>() => default;", // no parameters
                "public static void Create<T>(ReadOnlySpan<T> items) { }", // no return type
                "public static MyCollection<T> Create<T>(ReadOnlySpan<T> items, int index = 0) => default;", // optional parameter
                "public static MyCollection<T> Create<T>(ReadOnlySpan<T> items, params object[] args) => default;", // params
                "public static MyCollection<T> Create<T, U>(ReadOnlySpan<T> items) => default;", // extra type parameter
                "public static MyCollection<T> Create<T>(Span<T> items) => default;", // Span<T>
                "public static MyCollection<T> Create<T>(T[] items) => default;", // T[]
                "public static MyCollection<T> Create<T>(in ReadOnlySpan<T> items) => default;", // in parameter
                "public static MyCollection<T> Create<T>(ref ReadOnlySpan<T> items) => default;", // ref parameter
                "public static MyCollection<T> Create<T>(out ReadOnlySpan<T> items) { items = default; return default; }")] // out parameter
            string methodDeclaration,
            bool useCompilationReference)
        {
            string sourceA = $$"""
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    {{methodDeclaration}}
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,34): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<string> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 34),
                // (7,31): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[1, 2, 3]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(7, 31));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_UnexpectedSignature_MoreTypeParameters(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection : IEnumerable<object>
                {
                    IEnumerator<object> IEnumerable<object>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        MyCollection y = [1, 2, 3];
                        MyCollection z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<object>' and return type 'MyCollection'.
                //         MyCollection x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "object", "MyCollection").WithLocation(6, 26),
                // (7,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<object>' and return type 'MyCollection'.
                //         MyCollection y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[1, 2, 3]").WithArguments("Create", "object", "MyCollection").WithLocation(7, 26));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_UnexpectedSignature_FewerTypeParameters(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T, U> : IEnumerable<T>
                {
                    public IEnumerator<T> GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollection<T, int> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string, int> x = [];
                        MyCollection<int, string> y = [1, 2, 3];
                        MyCollection<int, object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,39): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T, U>'.
                //         MyCollection<string, int> x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T, U>").WithLocation(6, 39),
                // (7,39): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T, U>'.
                //         MyCollection<int, string> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[1, 2, 3]").WithArguments("Create", "T", "MyCollection<T, U>").WithLocation(7, 39));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_InheritedAttributeOnBaseCollection(bool useCompilationReference)
        {
            string sourceAttribute = """
                namespace System.Runtime.CompilerServices
                {
                    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
                    public sealed class CollectionBuilderAttribute : Attribute
                    {
                        public CollectionBuilderAttribute(Type builderType, string methodName) { }
                    }
                }
                """;
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public abstract class MyCollectionBase : IEnumerable<int>
                {
                    public IEnumerator<int> GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }
                public sealed class MyCollection : MyCollectionBase
                {
                }
                public sealed class MyCollectionBuilder
                {
                    public static MyCollectionBase Create(ReadOnlySpan<int> items) => new MyCollection();
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceAttribute }, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        MyCollection y = [2];
                        MyCollection z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,27): error CS1061: 'MyCollection' does not contain a definition for 'Add' and no accessible extension method 'Add' accepting a first argument of type 'MyCollection' could be found (are you missing a using directive or an assembly reference?)
                //         MyCollection y = [2];
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "2").WithArguments("MyCollection", "Add").WithLocation(6, 27));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_CreateMethodOnBase(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public sealed class MyCollection : IEnumerable<int>
                {
                    public IEnumerator<int> GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }
                public abstract class MyCollectionBuilderBase
                {
                    public static MyCollection Create(ReadOnlySpan<int> items) => new MyCollection();
                }
                public sealed class MyCollectionBuilder : MyCollectionBuilderBase
                {
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection x = [];
                        MyCollection y = [1, 2, 3];
                        MyCollection z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (5,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<int>' and return type 'MyCollection'.
                //         MyCollection x = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "int", "MyCollection").WithLocation(5, 26),
                // (6,26): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<int>' and return type 'MyCollection'.
                //         MyCollection y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[1, 2, 3]").WithArguments("Create", "int", "MyCollection").WithLocation(6, 26));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ObsoleteBuilderType_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                [Obsolete]
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,34): warning CS0612: 'MyCollectionBuilder' is obsolete
                //         MyCollection<string> x = [];
                Diagnostic(ErrorCode.WRN_DeprecatedSymbol, "[]").WithArguments("MyCollectionBuilder").WithLocation(6, 34),
                // (7,31): warning CS0612: 'MyCollectionBuilder' is obsolete
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.WRN_DeprecatedSymbol, "[1, 2, 3]").WithArguments("MyCollectionBuilder").WithLocation(7, 31));
        }

        [Fact]
        public void CollectionBuilder_ObsoleteBuilderType_02()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                [Obsolete("message 2", error: true)]
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(5,27): error CS0619: 'MyCollectionBuilder' is obsolete: 'message 2'
                // [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                Diagnostic(ErrorCode.ERR_DeprecatedSymbolStr, "MyCollectionBuilder").WithArguments("MyCollectionBuilder", "message 2").WithLocation(5, 27),
                // 1.cs(6,34): error CS0619: 'MyCollectionBuilder' is obsolete: 'message 2'
                //         MyCollection<string> x = [];
                Diagnostic(ErrorCode.ERR_DeprecatedSymbolStr, "[]").WithArguments("MyCollectionBuilder", "message 2").WithLocation(6, 34),
                // 1.cs(7,31): error CS0619: 'MyCollectionBuilder' is obsolete: 'message 2'
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_DeprecatedSymbolStr, "[1, 2, 3]").WithArguments("MyCollectionBuilder", "message 2").WithLocation(7, 31));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ObsoleteBuilderMethod_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    [Obsolete]
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,34): warning CS0612: 'MyCollectionBuilder.Create<T>(ReadOnlySpan<T>)' is obsolete
                //         MyCollection<string> x = [];
                Diagnostic(ErrorCode.WRN_DeprecatedSymbol, "[]").WithArguments("MyCollectionBuilder.Create<T>(System.ReadOnlySpan<T>)").WithLocation(6, 34),
                // (7,31): warning CS0612: 'MyCollectionBuilder.Create<T>(ReadOnlySpan<T>)' is obsolete
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.WRN_DeprecatedSymbol, "[1, 2, 3]").WithArguments("MyCollectionBuilder.Create<T>(System.ReadOnlySpan<T>)").WithLocation(7, 31));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_ObsoleteBuilderMethod_02(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    [Obsolete("message 4", error: true)]
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,34): error CS0619: 'MyCollectionBuilder.Create<T>(ReadOnlySpan<T>)' is obsolete: 'message 4'
                //         MyCollection<string> x = [];
                Diagnostic(ErrorCode.ERR_DeprecatedSymbolStr, "[]").WithArguments("MyCollectionBuilder.Create<T>(System.ReadOnlySpan<T>)", "message 4").WithLocation(6, 34),
                // (7,31): error CS0619: 'MyCollectionBuilder.Create<T>(ReadOnlySpan<T>)' is obsolete: 'message 4'
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_DeprecatedSymbolStr, "[1, 2, 3]").WithArguments("MyCollectionBuilder.Create<T>(System.ReadOnlySpan<T>)", "message 4").WithLocation(7, 31));
        }

        [Fact]
        public void CollectionBuilder_UnmanagedCallersOnly()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                using System.Runtime.InteropServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    [UnmanagedCallersOnly]
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<object> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 1.cs(6,34): error CS8901: 'MyCollectionBuilder.Create<string>(ReadOnlySpan<string>)' is attributed with 'UnmanagedCallersOnly' and cannot be called directly. Obtain a function pointer to this method.
                //         MyCollection<string> x = [];
                Diagnostic(ErrorCode.ERR_UnmanagedCallersOnlyMethodsCannotBeCalledDirectly, "[]").WithArguments("MyCollectionBuilder.Create<string>(System.ReadOnlySpan<string>)").WithLocation(6, 34),
                // 1.cs(7,31): error CS8901: 'MyCollectionBuilder.Create<int>(ReadOnlySpan<int>)' is attributed with 'UnmanagedCallersOnly' and cannot be called directly. Obtain a function pointer to this method.
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_UnmanagedCallersOnlyMethodsCannotBeCalledDirectly, "[1, 2, 3]").WithArguments("MyCollectionBuilder.Create<int>(System.ReadOnlySpan<int>)").WithLocation(7, 31),
                // 0.cs(14,6): error CS8895: Methods attributed with 'UnmanagedCallersOnly' cannot have generic type parameters and cannot be declared in a generic type.
                //     [UnmanagedCallersOnly]
                Diagnostic(ErrorCode.ERR_UnmanagedCallersOnlyMethodOrTypeCannotBeGeneric, "UnmanagedCallersOnly").WithLocation(14, 6),
                // 0.cs(15,45): error CS8894: Cannot use 'ReadOnlySpan<T>' as a parameter type on a method attributed with 'UnmanagedCallersOnly'.
                //     public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                Diagnostic(ErrorCode.ERR_CannotUseManagedTypeInUnmanagedCallersOnly, "ReadOnlySpan<T> items").WithArguments("System.ReadOnlySpan<T>", "parameter").WithLocation(15, 45));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_Constraints_CollectionAndBuilder(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T> where T : new()
                {
                    private List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) where T : struct
                        => new MyCollection<T>(new List<T>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB1 = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        x.Report();
                        MyCollection<int> y = [1, 2, 3];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB1, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));

            string sourceB2 = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int?> x = [4, null];
                        MyCollection<int?> y = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB2, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,32): error CS0453: The type 'int?' must be a non-nullable value type in order to use it as parameter 'T' in the generic type or method 'MyCollectionBuilder.Create<T>(ReadOnlySpan<T>)'
                //         MyCollection<int?> x = [4, null];
                Diagnostic(ErrorCode.ERR_ValConstraintNotSatisfied, "[4, null]").WithArguments("MyCollectionBuilder.Create<T>(System.ReadOnlySpan<T>)", "T", "int?").WithLocation(6, 32));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_Constraints_BuilderOnly(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) where T : struct
                        => new MyCollection<T>(new List<T>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB1 = """
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> x = [];
                        x.Report();
                        MyCollection<int> y = [1, 2, 3];
                        y.Report();
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB1, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.FailsPEVerify,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));

            string sourceB2 = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int?> x = [4, null];
                        MyCollection<int?> y = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB2, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,32): error CS0453: The type 'int?' must be a non-nullable value type in order to use it as parameter 'T' in the generic type or method 'MyCollectionBuilder.Create<T>(ReadOnlySpan<T>)'
                //         MyCollection<int?> x = [4, null];
                Diagnostic(ErrorCode.ERR_ValConstraintNotSatisfied, "[4, null]").WithArguments("MyCollectionBuilder.Create<T>(System.ReadOnlySpan<T>)", "T", "int?").WithLocation(6, 32));
        }

        [Fact]
        public void CollectionBuilder_Constraints_CollectionOnly()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T> where T : class
                {
                    private List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                        => default;
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<string> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 1.cs(7,22): error CS0452: The type 'int' must be a reference type in order to use it as parameter 'T' in the generic type or method 'MyCollection<T>'
                //         MyCollection<int> y = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_RefConstraintNotSatisfied, "int").WithArguments("MyCollection<T>", "T", "int").WithLocation(7, 22),
                // 0.cs(15,35): error CS0452: The type 'T' must be a reference type in order to use it as parameter 'T' in the generic type or method 'MyCollection<T>'
                //     public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                Diagnostic(ErrorCode.ERR_RefConstraintNotSatisfied, "Create").WithArguments("MyCollection<T>", "T", "T").WithLocation(15, 35));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_Substituted_01(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    public IEnumerator<T> GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        F([]);
                        F([1, 2, 3]);
                    }
                    static void F(MyCollection<int> c)
                    {
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics();

            var collectionType = (NamedTypeSymbol)comp.GetMember<MethodSymbol>("Program.F").Parameters[0].Type;
            Assert.Equal("MyCollection<System.Int32>", collectionType.ToTestDisplayString());
            TypeSymbol builderType;
            string methodName;
            Assert.True(collectionType.HasCollectionBuilderAttribute(out builderType, out methodName));
            Assert.Equal("MyCollectionBuilder", builderType.ToTestDisplayString());
            Assert.Equal("Create", methodName);

            var originalType = collectionType.OriginalDefinition;
            Assert.Equal("MyCollection<T>", originalType.ToTestDisplayString());
            Assert.True(originalType.HasCollectionBuilderAttribute(out builderType, out methodName));
            Assert.Equal("MyCollectionBuilder", builderType.ToTestDisplayString());
            Assert.Equal("Create", methodName);
        }

        [Fact]
        public void CollectionBuilder_Substituted_02()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(Container<string>.MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    public IEnumerator<T> GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class Container<T>
                {
                    public class MyCollectionBuilder
                    {
                        public static MyCollection<U> Create<U>(ReadOnlySpan<U> items) => default;
                    }
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        F([]);
                        F(new());
                    }
                    static void F(MyCollection<int> c)
                    {
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(5,2): error CS9185: The CollectionBuilderAttribute builder type must be a non-generic class or struct.
                // [CollectionBuilder(typeof(Container<string>.MyCollectionBuilder), "Create")]
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeInvalidType, "CollectionBuilder").WithLocation(5, 2),
                // 1.cs(6,11): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         F([]);
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 11));

            var collectionType = (NamedTypeSymbol)comp.GetMember<MethodSymbol>("Program.F").Parameters[0].Type;
            Assert.Equal("MyCollection<System.Int32>", collectionType.ToTestDisplayString());
            TypeSymbol builderType;
            string methodName;
            Assert.True(collectionType.HasCollectionBuilderAttribute(out builderType, out methodName));
            Assert.Equal("Container<System.String>.MyCollectionBuilder", builderType.ToTestDisplayString());
            Assert.Equal("Create", methodName);

            var originalType = collectionType.OriginalDefinition;
            Assert.Equal("MyCollection<T>", originalType.ToTestDisplayString());
            Assert.True(originalType.HasCollectionBuilderAttribute(out builderType, out methodName));
            Assert.Equal("Container<System.String>.MyCollectionBuilder", builderType.ToTestDisplayString());
            Assert.Equal("Create", methodName);
        }

        [Fact]
        public void CollectionBuilder_Substituted_03()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                public class Container<T>
                {
                    [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                    public struct MyCollection : IEnumerable<T>
                    {
                        public IEnumerator<T> GetEnumerator() => default;
                        IEnumerator IEnumerable.GetEnumerator() => default;
                    }
                    public class MyCollectionBuilder
                    {
                        public static MyCollection Create(ReadOnlySpan<T> items) => default;
                    }
                }
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        F([]);
                        F(new());
                    }
                    static void F(Container<string>.MyCollection c)
                    {
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(7,24): error CS0416: 'Container<T>.MyCollectionBuilder': an attribute argument cannot use type parameters
                //     [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                Diagnostic(ErrorCode.ERR_AttrArgWithTypeVars, "typeof(MyCollectionBuilder)").WithArguments("Container<T>.MyCollectionBuilder").WithLocation(7, 24));

            var collectionType = (NamedTypeSymbol)comp.GetMember<MethodSymbol>("Program.F").Parameters[0].Type;
            Assert.Equal("Container<System.String>.MyCollection", collectionType.ToTestDisplayString());
            Assert.False(collectionType.HasCollectionBuilderAttribute(out _, out _));

            var originalType = collectionType.OriginalDefinition;
            Assert.Equal("Container<T>.MyCollection", originalType.ToTestDisplayString());
            Assert.False(originalType.HasCollectionBuilderAttribute(out _, out _));
        }

        [Fact]
        public void CollectionBuilder_Retargeting()
        {
            string sourceA = """
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), "Create")]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    public IEnumerator<T> GetEnumerator() => default;
                    IEnumerator IEnumerable.GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    public static void Create(int[] items) { }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, CollectionBuilderAttributeDefinition }, targetFramework: TargetFramework.Mscorlib40);
            comp.VerifyEmitDiagnostics();
            var refA = comp.ToMetadataReference();

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        F([]);
                        F(new());
                    }
                    static void F(MyCollection<int> c)
                    {
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Mscorlib45);
            comp.VerifyEmitDiagnostics(
                // (6,11): error CS9187: Could not find an accessible 'Create' method with the expected signature: a static method with a single parameter of type 'ReadOnlySpan<T>' and return type 'MyCollection<T>'.
                //         F([]);
                Diagnostic(ErrorCode.ERR_CollectionBuilderAttributeMethodNotFound, "[]").WithArguments("Create", "T", "MyCollection<T>").WithLocation(6, 11));

            var collectionType = (NamedTypeSymbol)comp.GetMember<MethodSymbol>("Program.F").Parameters[0].Type;
            Assert.Equal("MyCollection<System.Int32>", collectionType.ToTestDisplayString());
            TypeSymbol builderType;
            string methodName;
            Assert.True(collectionType.HasCollectionBuilderAttribute(out builderType, out methodName));
            Assert.Equal("MyCollectionBuilder", builderType.ToTestDisplayString());
            Assert.Equal("Create", methodName);

            var retargetingType = (RetargetingNamedTypeSymbol)collectionType.OriginalDefinition;
            Assert.Equal("MyCollection<T>", retargetingType.ToTestDisplayString());
            Assert.True(retargetingType.HasCollectionBuilderAttribute(out builderType, out methodName));
            Assert.IsType<RetargetingNamedTypeSymbol>(builderType);
            Assert.Equal("MyCollectionBuilder", builderType.ToTestDisplayString());
            Assert.Equal("Create", methodName);
        }

        [Fact]
        public void CollectionBuilder_ExtensionMethodGetEnumerator_01()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                class MyCollection<T>
                {
                }
                class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                namespace N
                {
                    static class Extensions
                    {
                        public static IEnumerator<T> GetEnumerator<T>(this MyCollection<T> c) => default;
                        static MyCollection<T> F<T>() => [];
                    }
                }
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> c = [];
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(24,31): error CS9188: 'MyCollection<int>' has a CollectionBuilderAttribute but no element type.
                //         MyCollection<int> c = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderNoElementType, "[]").WithArguments("MyCollection<int>").WithLocation(24, 31));
        }

        [Fact]
        public void CollectionBuilder_ExtensionMethodGetEnumerator_02()
        {
            string sourceA = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public class MyCollection<T>
                {
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                static class Extensions
                {
                    public static IEnumerator<T> GetEnumerator<T>(this MyCollection<T> c) => default;
                    static MyCollection<T> F<T>() => [];
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics();
            var refA = comp.EmitToImageReference();

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> c = [];
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS9188: 'MyCollection<int>' has a CollectionBuilderAttribute but no element type.
                //         MyCollection<int> c = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderNoElementType, "[]").WithArguments("MyCollection<int>").WithLocation(6, 31));
        }

        [Fact]
        public void CollectionBuilder_InaccessibleGetEnumerator()
        {
            string source = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                class MyCollection<T>
                {
                    internal IEnumerator<T> GetEnumerator() => default;
                    public static MyCollection<T> F() => [];
                }
                class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                class Program
                {
                    static void Main()
                    {
                        MyCollection<int> c = [];
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(8,42): error CS9188: 'MyCollection<T>' has a CollectionBuilderAttribute but no element type.
                //     public static MyCollection<T> F() => [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderNoElementType, "[]").WithArguments("MyCollection<T>").WithLocation(8, 42),
                // 0.cs(18,31): error CS9188: 'MyCollection<int>' has a CollectionBuilderAttribute but no element type.
                //         MyCollection<int> c = [];
                Diagnostic(ErrorCode.ERR_CollectionBuilderNoElementType, "[]").WithArguments("MyCollection<int>").WithLocation(18, 31));
        }

        [InlineData("", "", false)]
        [InlineData("", "", true)]
        [InlineData("scoped", "", false)]
        [InlineData("scoped", "", true)]
        [InlineData("scoped", "scoped", false)]
        [InlineData("scoped", "scoped", true)]
        [Theory]
        public void CollectionBuilder_Scoped(string constructorParameterModifier, string builderParameterModifier, bool useCompilationReference)
        {
            string sourceA = $$"""
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public ref struct MyCollection<T>
                {
                    private readonly List<T> _list;
                    public MyCollection({{constructorParameterModifier}} ReadOnlySpan<T> items)
                    {
                        _list = new List<T>(items.ToArray());
                    }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>({{builderParameterModifier}} ReadOnlySpan<T> items) => new(items);
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics();
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        GetItems(x).Report();
                        MyCollection<int> y = [1, 2, 3];
                        GetItems(y).Report();
                    }
                    static List<T> GetItems<T>(MyCollection<T> c)
                    {
                        var list = new List<T>();
                        foreach (var i in c) list.Add(i);
                        return list;
                    }
                }
                """;
            CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
        }

        [Fact]
        public void CollectionBuilder_ScopedBuilderParameterOnly()
        {
            string sourceA = $$"""
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public ref struct MyCollection<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(ReadOnlySpan<T> items)
                    {
                        _list = new List<T>(items.ToArray());
                    }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(scoped ReadOnlySpan<T> items) => new(items);
                }
                """;
            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<int> y = [1, 2, 3];
                        MyCollection<string> z = new();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, sourceB }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(16,78): error CS8347: Cannot use a result of 'MyCollection<T>.MyCollection(ReadOnlySpan<T>)' in this context because it may expose variables referenced by parameter 'items' outside of their declaration scope
                //     public static MyCollection<T> Create<T>(scoped ReadOnlySpan<T> items) => new(items);
                Diagnostic(ErrorCode.ERR_EscapeCall, "new(items)").WithArguments("MyCollection<T>.MyCollection(System.ReadOnlySpan<T>)", "items").WithLocation(16, 78),
                // 0.cs(16,82): error CS8352: Cannot use variable 'scoped ReadOnlySpan<T> items' in this context because it may expose referenced variables outside of their declaration scope
                //     public static MyCollection<T> Create<T>(scoped ReadOnlySpan<T> items) => new(items);
                Diagnostic(ErrorCode.ERR_EscapeVariable, "items").WithArguments("scoped System.ReadOnlySpan<T> items").WithLocation(16, 82));
        }

        [CombinatorialData]
        [Theory]
        public void CollectionBuilder_MissingInt32(bool useCompilationReference)
        {
            string sourceA = """
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T>
                {
                    public IEnumerator<T> GetEnumerator() => default;
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => default;
                }
                """;
            var comp = CreateCompilation(sourceA, targetFramework: TargetFramework.Net80);
            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                #pragma warning disable 219
                class Program
                {
                    static void Main()
                    {
                        MyCollection<string> x = [];
                        MyCollection<string> y = ["2"];
                        MyCollection<object> z = new();
                    }
                }
                """;
            comp = CreateCompilation(sourceB, references: new[] { refA }, targetFramework: TargetFramework.Net80);
            comp.MakeTypeMissing(SpecialType.System_Int32);
            comp.VerifyEmitDiagnostics(
                // (7,34): error CS0518: Predefined type 'System.Int32' is not defined or imported
                //         MyCollection<string> y = ["2"];
                Diagnostic(ErrorCode.ERR_PredefinedTypeNotFound, @"[""2""]").WithArguments("System.Int32").WithLocation(7, 34),
                // (7,34): error CS0518: Predefined type 'System.Int32' is not defined or imported
                //         MyCollection<string> y = ["2"];
                Diagnostic(ErrorCode.ERR_PredefinedTypeNotFound, @"[""2""]").WithArguments("System.Int32").WithLocation(7, 34),
                // (7,34): error CS0518: Predefined type 'System.Int32' is not defined or imported
                //         MyCollection<string> y = ["2"];
                Diagnostic(ErrorCode.ERR_PredefinedTypeNotFound, @"[""2""]").WithArguments("System.Int32").WithLocation(7, 34));
        }

        [Fact]
        public void CollectionBuilder_Async()
        {
            string sourceA = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public struct MyCollection<T> : IEnumerable<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                    {
                        return new MyCollection<T>(new List<T>(items.ToArray()));
                    }
                }
                """;
            string sourceB = """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class Program
                {
                    static async Task Main()
                    {
                        (await CreateCollection()).Report();
                    }
                    static async Task<MyCollection<int>> CreateCollection()
                    {
                        return [await F(1), 2, await F(3)];
                    }
                    static async Task<int> F(int i)
                    {
                        await Task.Yield();
                        return i;
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { sourceA, sourceB, s_collectionExtensions },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("Program.<CreateCollection>d__1.System.Runtime.CompilerServices.IAsyncStateMachine.MoveNext()",
                """
                {
                  // Code size      324 (0x144)
                  .maxstack  3
                  .locals init (int V_0,
                                MyCollection<int> V_1,
                                int V_2,
                                int V_3,
                                System.Runtime.CompilerServices.TaskAwaiter<int> V_4,
                                System.Exception V_5)
                  IL_0000:  ldarg.0
                  IL_0001:  ldfld      "int Program.<CreateCollection>d__1.<>1__state"
                  IL_0006:  stloc.0
                  .try
                  {
                    IL_0007:  ldloc.0
                    IL_0008:  brfalse.s  IL_0057
                    IL_000a:  ldloc.0
                    IL_000b:  ldc.i4.1
                    IL_000c:  beq        IL_00cf
                    IL_0011:  ldarg.0
                    IL_0012:  ldflda     "<>y__InlineArray3<int> Program.<CreateCollection>d__1.<>7__wrap1"
                    IL_0017:  initobj    "<>y__InlineArray3<int>"
                    IL_001d:  ldc.i4.1
                    IL_001e:  call       "System.Threading.Tasks.Task<int> Program.F(int)"
                    IL_0023:  callvirt   "System.Runtime.CompilerServices.TaskAwaiter<int> System.Threading.Tasks.Task<int>.GetAwaiter()"
                    IL_0028:  stloc.s    V_4
                    IL_002a:  ldloca.s   V_4
                    IL_002c:  call       "bool System.Runtime.CompilerServices.TaskAwaiter<int>.IsCompleted.get"
                    IL_0031:  brtrue.s   IL_0074
                    IL_0033:  ldarg.0
                    IL_0034:  ldc.i4.0
                    IL_0035:  dup
                    IL_0036:  stloc.0
                    IL_0037:  stfld      "int Program.<CreateCollection>d__1.<>1__state"
                    IL_003c:  ldarg.0
                    IL_003d:  ldloc.s    V_4
                    IL_003f:  stfld      "System.Runtime.CompilerServices.TaskAwaiter<int> Program.<CreateCollection>d__1.<>u__1"
                    IL_0044:  ldarg.0
                    IL_0045:  ldflda     "System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>> Program.<CreateCollection>d__1.<>t__builder"
                    IL_004a:  ldloca.s   V_4
                    IL_004c:  ldarg.0
                    IL_004d:  call       "void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>, Program.<CreateCollection>d__1>(ref System.Runtime.CompilerServices.TaskAwaiter<int>, ref Program.<CreateCollection>d__1)"
                    IL_0052:  leave      IL_0143
                    IL_0057:  ldarg.0
                    IL_0058:  ldfld      "System.Runtime.CompilerServices.TaskAwaiter<int> Program.<CreateCollection>d__1.<>u__1"
                    IL_005d:  stloc.s    V_4
                    IL_005f:  ldarg.0
                    IL_0060:  ldflda     "System.Runtime.CompilerServices.TaskAwaiter<int> Program.<CreateCollection>d__1.<>u__1"
                    IL_0065:  initobj    "System.Runtime.CompilerServices.TaskAwaiter<int>"
                    IL_006b:  ldarg.0
                    IL_006c:  ldc.i4.m1
                    IL_006d:  dup
                    IL_006e:  stloc.0
                    IL_006f:  stfld      "int Program.<CreateCollection>d__1.<>1__state"
                    IL_0074:  ldloca.s   V_4
                    IL_0076:  call       "int System.Runtime.CompilerServices.TaskAwaiter<int>.GetResult()"
                    IL_007b:  stloc.2
                    IL_007c:  ldarg.0
                    IL_007d:  ldflda     "<>y__InlineArray3<int> Program.<CreateCollection>d__1.<>7__wrap1"
                    IL_0082:  ldc.i4.0
                    IL_0083:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                    IL_0088:  ldloc.2
                    IL_0089:  stind.i4
                    IL_008a:  ldarg.0
                    IL_008b:  ldflda     "<>y__InlineArray3<int> Program.<CreateCollection>d__1.<>7__wrap1"
                    IL_0090:  ldc.i4.1
                    IL_0091:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                    IL_0096:  ldc.i4.2
                    IL_0097:  stind.i4
                    IL_0098:  ldc.i4.3
                    IL_0099:  call       "System.Threading.Tasks.Task<int> Program.F(int)"
                    IL_009e:  callvirt   "System.Runtime.CompilerServices.TaskAwaiter<int> System.Threading.Tasks.Task<int>.GetAwaiter()"
                    IL_00a3:  stloc.s    V_4
                    IL_00a5:  ldloca.s   V_4
                    IL_00a7:  call       "bool System.Runtime.CompilerServices.TaskAwaiter<int>.IsCompleted.get"
                    IL_00ac:  brtrue.s   IL_00ec
                    IL_00ae:  ldarg.0
                    IL_00af:  ldc.i4.1
                    IL_00b0:  dup
                    IL_00b1:  stloc.0
                    IL_00b2:  stfld      "int Program.<CreateCollection>d__1.<>1__state"
                    IL_00b7:  ldarg.0
                    IL_00b8:  ldloc.s    V_4
                    IL_00ba:  stfld      "System.Runtime.CompilerServices.TaskAwaiter<int> Program.<CreateCollection>d__1.<>u__1"
                    IL_00bf:  ldarg.0
                    IL_00c0:  ldflda     "System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>> Program.<CreateCollection>d__1.<>t__builder"
                    IL_00c5:  ldloca.s   V_4
                    IL_00c7:  ldarg.0
                    IL_00c8:  call       "void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<int>, Program.<CreateCollection>d__1>(ref System.Runtime.CompilerServices.TaskAwaiter<int>, ref Program.<CreateCollection>d__1)"
                    IL_00cd:  leave.s    IL_0143
                    IL_00cf:  ldarg.0
                    IL_00d0:  ldfld      "System.Runtime.CompilerServices.TaskAwaiter<int> Program.<CreateCollection>d__1.<>u__1"
                    IL_00d5:  stloc.s    V_4
                    IL_00d7:  ldarg.0
                    IL_00d8:  ldflda     "System.Runtime.CompilerServices.TaskAwaiter<int> Program.<CreateCollection>d__1.<>u__1"
                    IL_00dd:  initobj    "System.Runtime.CompilerServices.TaskAwaiter<int>"
                    IL_00e3:  ldarg.0
                    IL_00e4:  ldc.i4.m1
                    IL_00e5:  dup
                    IL_00e6:  stloc.0
                    IL_00e7:  stfld      "int Program.<CreateCollection>d__1.<>1__state"
                    IL_00ec:  ldloca.s   V_4
                    IL_00ee:  call       "int System.Runtime.CompilerServices.TaskAwaiter<int>.GetResult()"
                    IL_00f3:  stloc.3
                    IL_00f4:  ldarg.0
                    IL_00f5:  ldflda     "<>y__InlineArray3<int> Program.<CreateCollection>d__1.<>7__wrap1"
                    IL_00fa:  ldc.i4.2
                    IL_00fb:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                    IL_0100:  ldloc.3
                    IL_0101:  stind.i4
                    IL_0102:  ldarg.0
                    IL_0103:  ldflda     "<>y__InlineArray3<int> Program.<CreateCollection>d__1.<>7__wrap1"
                    IL_0108:  ldc.i4.3
                    IL_0109:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<int>, int>(in <>y__InlineArray3<int>, int)"
                    IL_010e:  call       "MyCollection<int> MyCollectionBuilder.Create<int>(System.ReadOnlySpan<int>)"
                    IL_0113:  stloc.1
                    IL_0114:  leave.s    IL_012f
                  }
                  catch System.Exception
                  {
                    IL_0116:  stloc.s    V_5
                    IL_0118:  ldarg.0
                    IL_0119:  ldc.i4.s   -2
                    IL_011b:  stfld      "int Program.<CreateCollection>d__1.<>1__state"
                    IL_0120:  ldarg.0
                    IL_0121:  ldflda     "System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>> Program.<CreateCollection>d__1.<>t__builder"
                    IL_0126:  ldloc.s    V_5
                    IL_0128:  call       "void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>>.SetException(System.Exception)"
                    IL_012d:  leave.s    IL_0143
                  }
                  IL_012f:  ldarg.0
                  IL_0130:  ldc.i4.s   -2
                  IL_0132:  stfld      "int Program.<CreateCollection>d__1.<>1__state"
                  IL_0137:  ldarg.0
                  IL_0138:  ldflda     "System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>> Program.<CreateCollection>d__1.<>t__builder"
                  IL_013d:  ldloc.1
                  IL_013e:  call       "void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<MyCollection<int>>.SetResult(MyCollection<int>)"
                  IL_0143:  ret
                }
                """);
        }

        [Fact]
        public void CollectionBuilder_AttributeCycle()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;

                [CollectionBuilder(typeof(MyCollectionBuilder), MyCollectionBuilder.GetName([1, 2, 3]))]
                class MyCollection<T> : IEnumerable<T>
                {
                    public void Add(T t) { }
                    public IEnumerator<T> GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }

                static class MyCollectionBuilder
                {
                    public static string GetName<T>(MyCollection<T> c) => null;
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items) => null;
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(6,49): error CS0182: An attribute argument must be a constant expression, typeof expression or array creation expression of an attribute parameter type
                // [CollectionBuilder(typeof(MyCollectionBuilder), MyCollectionBuilder.GetName([1, 2, 3]))]
                Diagnostic(ErrorCode.ERR_BadAttributeArgument, "MyCollectionBuilder.GetName([1, 2, 3])").WithLocation(6, 49));
        }

        [ConditionalFact(typeof(DesktopOnly))]
        public void RestrictedTypes()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        var x = [default(TypedReference)];
                        var y = [default(ArgIterator)];
                        var z = [default(RuntimeArgumentHandle)];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (6,17): error CS9176: There is no target type for the collection expression.
                //         var x = [default(TypedReference)];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[default(TypedReference)]").WithLocation(6, 17),
                // (7,17): error CS9176: There is no target type for the collection expression.
                //         var y = [default(ArgIterator)];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[default(ArgIterator)]").WithLocation(7, 17),
                // (8,17): error CS9176: There is no target type for the collection expression.
                //         var z = [default(RuntimeArgumentHandle)];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[default(RuntimeArgumentHandle)]").WithLocation(8, 17));
        }

        [Fact]
        public void RefStruct_01()
        {
            string source = """
                ref struct R
                {
                    public R(ref int i) { }
                }
                class Program
                {
                    static void Main()
                    {
                        int i = 0;
                        var x = [default(R)];
                        var y = [new R(ref i)];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (10,17): error CS9176: There is no target type for the collection expression.
                //         var x = [default(R)];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[default(R)]").WithLocation(10, 17),
                // (11,17): error CS9176: There is no target type for the collection expression.
                //         var y = [new R(ref i)];
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[new R(ref i)]").WithLocation(11, 17));
        }

        [Fact]
        public void RefStruct_02()
        {
            string source = """
                using System.Collections.Generic;
                ref struct R
                {
                    public int _i;
                    public R(ref int i) { _i = i; }
                    public static implicit operator int(R r) => r._i;
                }
                class Program
                {
                    static void Main()
                    {
                        int i = 1;
                        int[] a = [default(R), new R(ref i)];
                        a.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[0, 1], ");
        }

        [Fact]
        public void RefStruct_03()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                class C : IEnumerable
                {
                    private List<int> _list = new List<int>();
                    public void Add(R r) { _list.Add(r._i); }
                    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
                }
                ref struct R
                {
                    public int _i;
                    public R(ref int i) { _i = i; }
                }
                class Program
                {
                    static void Main()
                    {
                        int i = 1;
                        C c = [default(R), new R(ref i)];
                        c.Report();
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[0, 1], ");
        }

        [CombinatorialData]
        [Theory]
        public void RefSafety_Return_01([CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F1<int>().Report();
                        F2<string>().Report();
                    }
                    static Span<T> F1<T>() => [];
                    static ReadOnlySpan<T> F2<T>() => [];
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: targetFramework,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[], [], "));
            verifier.VerifyIL("Program.F1<T>", """
                {
                  // Code size       11 (0xb)
                  .maxstack  1
                  IL_0000:  call       "T[] System.Array.Empty<T>()"
                  IL_0005:  newobj     "System.Span<T>..ctor(T[])"
                  IL_000a:  ret
                }
                """);
            verifier.VerifyIL("Program.F2<T>", """
                {
                  // Code size       11 (0xb)
                  .maxstack  1
                  IL_0000:  call       "T[] System.Array.Empty<T>()"
                  IL_0005:  newobj     "System.ReadOnlySpan<T>..ctor(T[])"
                  IL_000a:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void RefSafety_Return_02([CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                using System.Collections.Generic;
                class Program
                {
                    static Span<T> F1<T>(T x, T y) => [x, y];
                    static ReadOnlySpan<T> F2<T>(T x, T y) => [x, y];
                    static ReadOnlySpan<T> F3<T>(IEnumerable<T> e) => [..e];
                }
                """;
            var comp = CreateCompilation(source, targetFramework: targetFramework);
            comp.VerifyEmitDiagnostics(
                // (5,39): error CS9203: A collection expression of type 'Span<T>' cannot be used in this context because it may be exposed outside of the current scope.
                //     static Span<T> F1<T>(T x, T y) => [x, y];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[x, y]").WithArguments("System.Span<T>").WithLocation(5, 39),
                // (6,47): error CS9203: A collection expression of type 'ReadOnlySpan<T>' cannot be used in this context because it may be exposed outside of the current scope.
                //     static ReadOnlySpan<T> F2<T>(T x, T y) => [x, y];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[x, y]").WithArguments("System.ReadOnlySpan<T>").WithLocation(6, 47),
                // (7,55): error CS9203: A collection expression of type 'ReadOnlySpan<T>' cannot be used in this context because it may be exposed outside of the current scope.
                //     static ReadOnlySpan<T> F3<T>(IEnumerable<T> e) => [..e];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[..e]").WithArguments("System.ReadOnlySpan<T>").WithLocation(7, 55));
        }

        [CombinatorialData]
        [Theory]
        public void RefSafety_Return_03([CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F1<int>(1, 2).Report();
                        F2<string>("3", null).Report();
                    }
                    static Span<T> F1<T>(T x, T y) => (T[])[x, y];
                    static ReadOnlySpan<T> F2<T>(T x, T y) => (T[])[x, y];
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: targetFramework,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2], [3, null], "));
            verifier.VerifyIL("Program.F1<T>", """
                {
                    // Code size       28 (0x1c)
                    .maxstack  4
                    IL_0000:  ldc.i4.2
                    IL_0001:  newarr     "T"
                    IL_0006:  dup
                    IL_0007:  ldc.i4.0
                    IL_0008:  ldarg.0
                    IL_0009:  stelem     "T"
                    IL_000e:  dup
                    IL_000f:  ldc.i4.1
                    IL_0010:  ldarg.1
                    IL_0011:  stelem     "T"
                    IL_0016:  call       "System.Span<T> System.Span<T>.op_Implicit(T[])"
                    IL_001b:  ret
                }
                """);
            verifier.VerifyIL("Program.F2<T>", """
                {
                  // Code size       28 (0x1c)
                  .maxstack  4
                  IL_0000:  ldc.i4.2
                  IL_0001:  newarr     "T"
                  IL_0006:  dup
                  IL_0007:  ldc.i4.0
                  IL_0008:  ldarg.0
                  IL_0009:  stelem     "T"
                  IL_000e:  dup
                  IL_000f:  ldc.i4.1
                  IL_0010:  ldarg.1
                  IL_0011:  stelem     "T"
                  IL_0016:  call       "System.ReadOnlySpan<T> System.ReadOnlySpan<T>.op_Implicit(T[])"
                  IL_001b:  ret
                }
                """);
        }

        [Fact]
        public void RefSafety_Return_04()
        {
            string source = """
                using System;
                delegate Span<T> D<T>();
                class Program
                {
                    static void Main()
                    {
                        D<int> d = () => [1, 2, 3];
                        Span<int> s = d();
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (7,26): error CS9203: A collection expression of type 'Span<int>' cannot be used in this context because it may be exposed outside of the current scope.
                //         D<int> d = () => [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[1, 2, 3]").WithArguments("System.Span<int>").WithLocation(7, 26));
        }

        [CombinatorialData]
        [Theory]
        public void RefSafety_RefStruct(
            [CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework,
            bool useScoped,
            bool useUnsafe)
        {
            string sourceA = $$"""
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public ref struct MyCollection<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>({{(useScoped ? "scoped" : "")}} ReadOnlySpan<T> items)
                        => new MyCollection<T>(new List<T>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(
                targetFramework == TargetFramework.Net80 ? new[] { sourceA } : new[] { sourceA, CollectionBuilderAttributeDefinition },
                targetFramework: targetFramework);
            comp.VerifyEmitDiagnostics();
            var refA = comp.EmitToImageReference();

            string sourceB = $$"""
                using System.Collections.Generic;
                {{(useUnsafe ? "unsafe" : "")}} class Program
                {
                    static void Main()
                    {
                        MyCollection<object> x = Empty<object>();
                        MyCollection<object> y = ThreeItems<object>(1, 2, 3);
                        Report(x);
                        Report(y);
                    }
                    static MyCollection<T> Empty<T>() => [];
                    static MyCollection<T> ThreeItems<T>(T x, T y, T z) => [x, y, z];
                    static void Report<T>(MyCollection<T> c)
                    {
                        var list = new List<T>();
                        foreach (var i in c) list.Add(i);
                        list.Report();
                    }
                }
                """;
            comp = CreateCompilation(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: targetFramework,
                options: useUnsafe ? TestOptions.UnsafeReleaseExe : TestOptions.ReleaseExe);
            if (!useScoped)
            {
                comp.VerifyEmitDiagnostics(
                    // 0.cs(12,60): error CS9203: A collection expression of type 'MyCollection<T>' cannot be used in this context because it may be exposed outside of the current scope.
                    //     static MyCollection<T> ThreeItems<T>(T x, T y, T z) => [x, y, z];
                    Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[x, y, z]").WithArguments("MyCollection<T>").WithLocation(12, 60));
            }
            else
            {
                var verifier = CompileAndVerify(comp,
                    verify: Verification.Skipped,
                    expectedOutput: IncludeExpectedOutput("[], [1, 2, 3], "));
                verifier.VerifyIL("Program.Empty<T>", """
                    {
                      // Code size       16 (0x10)
                      .maxstack  1
                      IL_0000:  call       "T[] System.Array.Empty<T>()"
                      IL_0005:  newobj     "System.ReadOnlySpan<T>..ctor(T[])"
                      IL_000a:  call       "MyCollection<T> MyCollectionBuilder.Create<T>(scoped System.ReadOnlySpan<T>)"
                      IL_000f:  ret
                    }
                    """);
                if (targetFramework == TargetFramework.Net80)
                {
                    verifier.VerifyIL("Program.ThreeItems<T>", """
                        {
                          // Code size       64 (0x40)
                          .maxstack  2
                          .locals init (<>y__InlineArray3<T> V_0)
                          IL_0000:  ldloca.s   V_0
                          IL_0002:  initobj    "<>y__InlineArray3<T>"
                          IL_0008:  ldloca.s   V_0
                          IL_000a:  ldc.i4.0
                          IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                          IL_0010:  ldarg.0
                          IL_0011:  stobj      "T"
                          IL_0016:  ldloca.s   V_0
                          IL_0018:  ldc.i4.1
                          IL_0019:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                          IL_001e:  ldarg.1
                          IL_001f:  stobj      "T"
                          IL_0024:  ldloca.s   V_0
                          IL_0026:  ldc.i4.2
                          IL_0027:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                          IL_002c:  ldarg.2
                          IL_002d:  stobj      "T"
                          IL_0032:  ldloca.s   V_0
                          IL_0034:  ldc.i4.3
                          IL_0035:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<T>, T>(in <>y__InlineArray3<T>, int)"
                          IL_003a:  call       "MyCollection<T> MyCollectionBuilder.Create<T>(scoped System.ReadOnlySpan<T>)"
                          IL_003f:  ret
                        }
                        """);
                }
                else
                {
                    verifier.VerifyIL("Program.ThreeItems<T>", """
                        {
                          // Code size       41 (0x29)
                          .maxstack  4
                          IL_0000:  ldc.i4.3
                          IL_0001:  newarr     "T"
                          IL_0006:  dup
                          IL_0007:  ldc.i4.0
                          IL_0008:  ldarg.0
                          IL_0009:  stelem     "T"
                          IL_000e:  dup
                          IL_000f:  ldc.i4.1
                          IL_0010:  ldarg.1
                          IL_0011:  stelem     "T"
                          IL_0016:  dup
                          IL_0017:  ldc.i4.2
                          IL_0018:  ldarg.2
                          IL_0019:  stelem     "T"
                          IL_001e:  newobj     "System.ReadOnlySpan<T>..ctor(T[])"
                          IL_0023:  call       "MyCollection<T> MyCollectionBuilder.Create<T>(scoped System.ReadOnlySpan<T>)"
                          IL_0028:  ret
                        }
                        """);
                }
            }
        }

        // As above, but with C#10 ref safety rules.
        [Theory]
        [CombinatorialData]
        public void RefSafety_RefStruct_CSharp10Rules(bool useCompilationReference)
        {
            string sourceA = $$"""
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public ref struct MyCollection<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                        => new MyCollection<T>(new List<T>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(new[] { sourceA, CollectionBuilderAttributeDefinition }, parseOptions: TestOptions.Regular.WithLanguageVersion(LanguageVersion.CSharp10), targetFramework: TargetFramework.Net60);
            comp.VerifyEmitDiagnostics();
            Assert.False(comp.SourceModule.UseUpdatedEscapeRules);

            var refA = AsReference(comp, useCompilationReference);

            string sourceB = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        MyCollection<object> x = Empty<object>();
                        MyCollection<object> y = ThreeItems<object>(1, 2, 3);
                        Report(x);
                        Report(y);
                    }
                    static MyCollection<T> Empty<T>() => [];
                    static MyCollection<T> ThreeItems<T>(T x, T y, T z) => [x, y, z];
                    static void Report<T>(MyCollection<T> c)
                    {
                        var list = new List<T>();
                        foreach (var i in c) list.Add(i);
                        list.Report();
                    }
                }
                """;
            comp = CreateCompilation(new[] { sourceB, s_collectionExtensions }, references: new[] { refA }, targetFramework: TargetFramework.Net60, options: TestOptions.ReleaseExe);
            comp.VerifyEmitDiagnostics(
                // 0.cs(12,60): error CS9203: A collection expression of type 'MyCollection<T>' cannot be used in this context because it may be exposed outside of the current scope.
                //     static MyCollection<T> ThreeItems<T>(T x, T y, T z) => [x, y, z];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[x, y, z]").WithArguments("MyCollection<T>").WithLocation(12, 60));
        }

        [CombinatorialData]
        [Theory]
        public void SpanArgument_01([CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F1<object>([1]);
                        F2<int?>([2]);
                        F3<int?>([3]);
                        F4<object>([4]);
                    }
                    static void F1<T>(Span<T> s) { s.Report(); }
                    static void F2<T>(ReadOnlySpan<T> s) { s.Report(); }
                    static void F3<T>(in Span<T> s) { s.Report(); }
                    static void F4<T>(in ReadOnlySpan<T> s) { s.Report(); }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: targetFramework,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [2], [3], [4], "));
            if (targetFramework == TargetFramework.Net80)
            {
                verifier.VerifyIL("Program.Main", """
                    {
                      // Code size      161 (0xa1)
                      .maxstack  2
                      .locals init (<>y__InlineArray1<object> V_0,
                                    <>y__InlineArray1<int?> V_1,
                                    <>y__InlineArray1<int?> V_2,
                                    <>y__InlineArray1<object> V_3,
                                    System.Span<int?> V_4,
                                    System.ReadOnlySpan<object> V_5)
                      IL_0000:  ldloca.s   V_0
                      IL_0002:  initobj    "<>y__InlineArray1<object>"
                      IL_0008:  ldloca.s   V_0
                      IL_000a:  ldc.i4.0
                      IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0010:  ldc.i4.1
                      IL_0011:  box        "int"
                      IL_0016:  stind.ref
                      IL_0017:  ldloca.s   V_0
                      IL_0019:  ldc.i4.1
                      IL_001a:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_001f:  call       "void Program.F1<object>(System.Span<object>)"
                      IL_0024:  ldloca.s   V_1
                      IL_0026:  initobj    "<>y__InlineArray1<int?>"
                      IL_002c:  ldloca.s   V_1
                      IL_002e:  ldc.i4.0
                      IL_002f:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                      IL_0034:  ldc.i4.2
                      IL_0035:  newobj     "int?..ctor(int)"
                      IL_003a:  stobj      "int?"
                      IL_003f:  ldloca.s   V_1
                      IL_0041:  ldc.i4.1
                      IL_0042:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                      IL_0047:  call       "void Program.F2<int?>(System.ReadOnlySpan<int?>)"
                      IL_004c:  ldloca.s   V_2
                      IL_004e:  initobj    "<>y__InlineArray1<int?>"
                      IL_0054:  ldloca.s   V_2
                      IL_0056:  ldc.i4.0
                      IL_0057:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                      IL_005c:  ldc.i4.3
                      IL_005d:  newobj     "int?..ctor(int)"
                      IL_0062:  stobj      "int?"
                      IL_0067:  ldloca.s   V_2
                      IL_0069:  ldc.i4.1
                      IL_006a:  call       "InlineArrayAsSpan<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                      IL_006f:  stloc.s    V_4
                      IL_0071:  ldloca.s   V_4
                      IL_0073:  call       "void Program.F3<int?>(in System.Span<int?>)"
                      IL_0078:  ldloca.s   V_3
                      IL_007a:  initobj    "<>y__InlineArray1<object>"
                      IL_0080:  ldloca.s   V_3
                      IL_0082:  ldc.i4.0
                      IL_0083:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0088:  ldc.i4.4
                      IL_0089:  box        "int"
                      IL_008e:  stind.ref
                      IL_008f:  ldloca.s   V_3
                      IL_0091:  ldc.i4.1
                      IL_0092:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                      IL_0097:  stloc.s    V_5
                      IL_0099:  ldloca.s   V_5
                      IL_009b:  call       "void Program.F4<object>(in System.ReadOnlySpan<object>)"
                      IL_00a0:  ret
                    }
                    """);
            }
            else
            {
                verifier.VerifyIL("Program.Main", """
                    {
                      // Code size      115 (0x73)
                      .maxstack  4
                      .locals init (System.Span<int?> V_0,
                                    System.ReadOnlySpan<object> V_1)
                      IL_0000:  ldc.i4.1
                      IL_0001:  newarr     "object"
                      IL_0006:  dup
                      IL_0007:  ldc.i4.0
                      IL_0008:  ldc.i4.1
                      IL_0009:  box        "int"
                      IL_000e:  stelem.ref
                      IL_000f:  newobj     "System.Span<object>..ctor(object[])"
                      IL_0014:  call       "void Program.F1<object>(System.Span<object>)"
                      IL_0019:  ldc.i4.1
                      IL_001a:  newarr     "int?"
                      IL_001f:  dup
                      IL_0020:  ldc.i4.0
                      IL_0021:  ldc.i4.2
                      IL_0022:  newobj     "int?..ctor(int)"
                      IL_0027:  stelem     "int?"
                      IL_002c:  newobj     "System.ReadOnlySpan<int?>..ctor(int?[])"
                      IL_0031:  call       "void Program.F2<int?>(System.ReadOnlySpan<int?>)"
                      IL_0036:  ldc.i4.1
                      IL_0037:  newarr     "int?"
                      IL_003c:  dup
                      IL_003d:  ldc.i4.0
                      IL_003e:  ldc.i4.3
                      IL_003f:  newobj     "int?..ctor(int)"
                      IL_0044:  stelem     "int?"
                      IL_0049:  newobj     "System.Span<int?>..ctor(int?[])"
                      IL_004e:  stloc.0
                      IL_004f:  ldloca.s   V_0
                      IL_0051:  call       "void Program.F3<int?>(in System.Span<int?>)"
                      IL_0056:  ldc.i4.1
                      IL_0057:  newarr     "object"
                      IL_005c:  dup
                      IL_005d:  ldc.i4.0
                      IL_005e:  ldc.i4.4
                      IL_005f:  box        "int"
                      IL_0064:  stelem.ref
                      IL_0065:  newobj     "System.ReadOnlySpan<object>..ctor(object[])"
                      IL_006a:  stloc.1
                      IL_006b:  ldloca.s   V_1
                      IL_006d:  call       "void Program.F4<object>(in System.ReadOnlySpan<object>)"
                      IL_0072:  ret
                    }
                    """);
            }
        }

        [Fact]
        public void SpanArgument_02()
        {
            string source = """
                using System;
                struct S { }
                ref struct R { }
                class Program
                {
                    static void Main()
                    {
                        ReturnsStruct<object>([1]);
                        ReturnsRefStruct<object>([2]);
                        ReturnsRef<object>([3]);
                        ReturnsRefReadOnly<object>([4]);
                    }
                    static int _f = 0;
                    static S ReturnsStruct<T>(Span<T> s) { s.Report(); return default; }
                    static R ReturnsRefStruct<T>(Span<T> s) { s.Report(); return default; }
                    static ref int ReturnsRef<T>(Span<T> s) { s.Report(); return ref _f; }
                    static ref readonly int ReturnsRefReadOnly<T>(Span<T> s) { s.Report(); return ref _f; }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [2], [3], [4], "));
            verifier.VerifyIL("Program.Main", """
                {
                    // Code size      149 (0x95)
                    .maxstack  2
                    .locals init (<>y__InlineArray1<object> V_0,
                                <>y__InlineArray1<object> V_1,
                                <>y__InlineArray1<object> V_2,
                                <>y__InlineArray1<object> V_3)
                    IL_0000:  ldloca.s   V_0
                    IL_0002:  initobj    "<>y__InlineArray1<object>"
                    IL_0008:  ldloca.s   V_0
                    IL_000a:  ldc.i4.0
                    IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0010:  ldc.i4.1
                    IL_0011:  box        "int"
                    IL_0016:  stind.ref
                    IL_0017:  ldloca.s   V_0
                    IL_0019:  ldc.i4.1
                    IL_001a:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_001f:  call       "S Program.ReturnsStruct<object>(System.Span<object>)"
                    IL_0024:  pop
                    IL_0025:  ldloca.s   V_1
                    IL_0027:  initobj    "<>y__InlineArray1<object>"
                    IL_002d:  ldloca.s   V_1
                    IL_002f:  ldc.i4.0
                    IL_0030:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0035:  ldc.i4.2
                    IL_0036:  box        "int"
                    IL_003b:  stind.ref
                    IL_003c:  ldloca.s   V_1
                    IL_003e:  ldc.i4.1
                    IL_003f:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0044:  call       "R Program.ReturnsRefStruct<object>(System.Span<object>)"
                    IL_0049:  pop
                    IL_004a:  ldloca.s   V_2
                    IL_004c:  initobj    "<>y__InlineArray1<object>"
                    IL_0052:  ldloca.s   V_2
                    IL_0054:  ldc.i4.0
                    IL_0055:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_005a:  ldc.i4.3
                    IL_005b:  box        "int"
                    IL_0060:  stind.ref
                    IL_0061:  ldloca.s   V_2
                    IL_0063:  ldc.i4.1
                    IL_0064:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0069:  call       "ref int Program.ReturnsRef<object>(System.Span<object>)"
                    IL_006e:  pop
                    IL_006f:  ldloca.s   V_3
                    IL_0071:  initobj    "<>y__InlineArray1<object>"
                    IL_0077:  ldloca.s   V_3
                    IL_0079:  ldc.i4.0
                    IL_007a:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_007f:  ldc.i4.4
                    IL_0080:  box        "int"
                    IL_0085:  stind.ref
                    IL_0086:  ldloca.s   V_3
                    IL_0088:  ldc.i4.1
                    IL_0089:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_008e:  call       "ref readonly int Program.ReturnsRefReadOnly<object>(System.Span<object>)"
                    IL_0093:  pop
                    IL_0094:  ret
                }
                """);
        }

        [Fact]
        public void SpanArgument_03()
        {
            string source = """
                using System;
                struct S { }
                ref struct R { }
                class Program
                {
                    static void Main()
                    {
                        ReturnsRefStruct<object>([2]);
                        ReturnsRef<object>([3]);
                        ReturnsRefReadOnly<object>([4]);
                    }
                    static int _f = 0;
                    static R ReturnsRefStruct<T>(scoped Span<T> s) { s.Report(); return default; }
                    static ref int ReturnsRef<T>(scoped Span<T> s) { s.Report(); return ref _f; }
                    static ref readonly int ReturnsRefReadOnly<T>(scoped Span<T> s) { s.Report(); return ref _f; }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[2], [3], [4], "));
            verifier.VerifyIL("Program.Main", """
                {
                    // Code size      112 (0x70)
                    .maxstack  2
                    .locals init (<>y__InlineArray1<object> V_0,
                                <>y__InlineArray1<object> V_1,
                                <>y__InlineArray1<object> V_2)
                    IL_0000:  ldloca.s   V_0
                    IL_0002:  initobj    "<>y__InlineArray1<object>"
                    IL_0008:  ldloca.s   V_0
                    IL_000a:  ldc.i4.0
                    IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0010:  ldc.i4.2
                    IL_0011:  box        "int"
                    IL_0016:  stind.ref
                    IL_0017:  ldloca.s   V_0
                    IL_0019:  ldc.i4.1
                    IL_001a:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_001f:  call       "R Program.ReturnsRefStruct<object>(scoped System.Span<object>)"
                    IL_0024:  pop
                    IL_0025:  ldloca.s   V_1
                    IL_0027:  initobj    "<>y__InlineArray1<object>"
                    IL_002d:  ldloca.s   V_1
                    IL_002f:  ldc.i4.0
                    IL_0030:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0035:  ldc.i4.3
                    IL_0036:  box        "int"
                    IL_003b:  stind.ref
                    IL_003c:  ldloca.s   V_1
                    IL_003e:  ldc.i4.1
                    IL_003f:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0044:  call       "ref int Program.ReturnsRef<object>(scoped System.Span<object>)"
                    IL_0049:  pop
                    IL_004a:  ldloca.s   V_2
                    IL_004c:  initobj    "<>y__InlineArray1<object>"
                    IL_0052:  ldloca.s   V_2
                    IL_0054:  ldc.i4.0
                    IL_0055:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_005a:  ldc.i4.4
                    IL_005b:  box        "int"
                    IL_0060:  stind.ref
                    IL_0061:  ldloca.s   V_2
                    IL_0063:  ldc.i4.1
                    IL_0064:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0069:  call       "ref readonly int Program.ReturnsRefReadOnly<object>(scoped System.Span<object>)"
                    IL_006e:  pop
                    IL_006f:  ret
                }
                """);
        }

        [Fact]
        public void SpanArgument_04()
        {
            string source = """
                using System;
                ref struct R1
                {
                    public void M(ReadOnlySpan<int?> s) { s.Report(); }
                    public object this[ReadOnlySpan<int?> s] { set { s.Report(); } }
                }
                class Program
                {
                    static void Main()
                    {
                        var r1 = new R1();
                        r1.M([3]);
                        r1[[4]] = null;

                    }
                }
                """;
            var comp = CreateCompilation(new[] { source, s_collectionExtensionsWithSpan }, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // 0.cs(12,9): error CS8350: This combination of arguments to 'R1.M(ReadOnlySpan<int?>)' is disallowed because it may expose variables referenced by parameter 's' outside of their declaration scope
                //         r1.M([3]);
                Diagnostic(ErrorCode.ERR_CallArgMixing, "r1.M([3])").WithArguments("R1.M(System.ReadOnlySpan<int?>)", "s").WithLocation(12, 9),
                // 0.cs(12,14): error CS9203: A collection expression of type 'ReadOnlySpan<int?>' cannot be used in this context because it may be exposed outside of the current scope.
                //         r1.M([3]);
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[3]").WithArguments("System.ReadOnlySpan<int?>").WithLocation(12, 14),
                // 0.cs(13,9): error CS8350: This combination of arguments to 'R1.this[ReadOnlySpan<int?>]' is disallowed because it may expose variables referenced by parameter 's' outside of their declaration scope
                //         r1[[4]] = null;
                Diagnostic(ErrorCode.ERR_CallArgMixing, "r1[[4]]").WithArguments("R1.this[System.ReadOnlySpan<int?>]", "s").WithLocation(13, 9),
                // 0.cs(13,12): error CS9203: A collection expression of type 'ReadOnlySpan<int?>' cannot be used in this context because it may be exposed outside of the current scope.
                //         r1[[4]] = null;
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[4]").WithArguments("System.ReadOnlySpan<int?>").WithLocation(13, 12));
        }

        [Fact]
        public void SpanArgument_05()
        {
            string source = """
                using System;
                struct S
                {
                    public void M(ReadOnlySpan<int?> s) { s.Report(); }
                    public object this[ReadOnlySpan<int?> s] { set { s.Report(); } }
                }
                ref struct R1
                {
                    public void M(ReadOnlySpan<int?> s) { s.Report(); }
                    public object this[ReadOnlySpan<int?> s] { set { s.Report(); } }
                }
                ref struct R2
                {
                    public void M(scoped ReadOnlySpan<int?> s) { s.Report(); }
                    public object this[scoped ReadOnlySpan<int?> s] { set { s.Report(); } }
                }
                class Program
                {
                    static void Main()
                    {
                        var s = new S();
                        s.M([1]);
                        s[[2]] = null;
                        scoped var r1 = new R1();
                        r1.M([3]);
                        r1[[4]] = null;
                        var r2 = new R2();
                        r2.M([5]);
                        r2[[6]] = null;
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [2], [3], [4], [5], [6], "));
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size      280 (0x118)
                  .maxstack  3
                  .locals init (S V_0, //s
                                R1 V_1, //r1
                                R2 V_2, //r2
                                <>y__InlineArray1<int?> V_3,
                                <>y__InlineArray1<int?> V_4,
                                <>y__InlineArray1<int?> V_5,
                                <>y__InlineArray1<int?> V_6,
                                <>y__InlineArray1<int?> V_7,
                                <>y__InlineArray1<int?> V_8)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "S"
                  IL_0008:  ldloca.s   V_0
                  IL_000a:  ldloca.s   V_3
                  IL_000c:  initobj    "<>y__InlineArray1<int?>"
                  IL_0012:  ldloca.s   V_3
                  IL_0014:  ldc.i4.0
                  IL_0015:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_001a:  ldc.i4.1
                  IL_001b:  newobj     "int?..ctor(int)"
                  IL_0020:  stobj      "int?"
                  IL_0025:  ldloca.s   V_3
                  IL_0027:  ldc.i4.1
                  IL_0028:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_002d:  call       "void S.M(System.ReadOnlySpan<int?>)"
                  IL_0032:  ldloca.s   V_0
                  IL_0034:  ldloca.s   V_4
                  IL_0036:  initobj    "<>y__InlineArray1<int?>"
                  IL_003c:  ldloca.s   V_4
                  IL_003e:  ldc.i4.0
                  IL_003f:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_0044:  ldc.i4.2
                  IL_0045:  newobj     "int?..ctor(int)"
                  IL_004a:  stobj      "int?"
                  IL_004f:  ldloca.s   V_4
                  IL_0051:  ldc.i4.1
                  IL_0052:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_0057:  ldnull
                  IL_0058:  call       "void S.this[System.ReadOnlySpan<int?>].set"
                  IL_005d:  ldloca.s   V_1
                  IL_005f:  initobj    "R1"
                  IL_0065:  ldloca.s   V_1
                  IL_0067:  ldloca.s   V_5
                  IL_0069:  initobj    "<>y__InlineArray1<int?>"
                  IL_006f:  ldloca.s   V_5
                  IL_0071:  ldc.i4.0
                  IL_0072:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_0077:  ldc.i4.3
                  IL_0078:  newobj     "int?..ctor(int)"
                  IL_007d:  stobj      "int?"
                  IL_0082:  ldloca.s   V_5
                  IL_0084:  ldc.i4.1
                  IL_0085:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_008a:  call       "void R1.M(System.ReadOnlySpan<int?>)"
                  IL_008f:  ldloca.s   V_1
                  IL_0091:  ldloca.s   V_6
                  IL_0093:  initobj    "<>y__InlineArray1<int?>"
                  IL_0099:  ldloca.s   V_6
                  IL_009b:  ldc.i4.0
                  IL_009c:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_00a1:  ldc.i4.4
                  IL_00a2:  newobj     "int?..ctor(int)"
                  IL_00a7:  stobj      "int?"
                  IL_00ac:  ldloca.s   V_6
                  IL_00ae:  ldc.i4.1
                  IL_00af:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_00b4:  ldnull
                  IL_00b5:  call       "void R1.this[System.ReadOnlySpan<int?>].set"
                  IL_00ba:  ldloca.s   V_2
                  IL_00bc:  initobj    "R2"
                  IL_00c2:  ldloca.s   V_2
                  IL_00c4:  ldloca.s   V_7
                  IL_00c6:  initobj    "<>y__InlineArray1<int?>"
                  IL_00cc:  ldloca.s   V_7
                  IL_00ce:  ldc.i4.0
                  IL_00cf:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_00d4:  ldc.i4.5
                  IL_00d5:  newobj     "int?..ctor(int)"
                  IL_00da:  stobj      "int?"
                  IL_00df:  ldloca.s   V_7
                  IL_00e1:  ldc.i4.1
                  IL_00e2:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_00e7:  call       "void R2.M(scoped System.ReadOnlySpan<int?>)"
                  IL_00ec:  ldloca.s   V_2
                  IL_00ee:  ldloca.s   V_8
                  IL_00f0:  initobj    "<>y__InlineArray1<int?>"
                  IL_00f6:  ldloca.s   V_8
                  IL_00f8:  ldc.i4.0
                  IL_00f9:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_00fe:  ldc.i4.6
                  IL_00ff:  newobj     "int?..ctor(int)"
                  IL_0104:  stobj      "int?"
                  IL_0109:  ldloca.s   V_8
                  IL_010b:  ldc.i4.1
                  IL_010c:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_0111:  ldnull
                  IL_0112:  call       "void R2.this[scoped System.ReadOnlySpan<int?>].set"
                  IL_0117:  ret
                }
                """);
        }

        [Fact]
        public void SpanArgument_ReadOnlyMembers()
        {
            string source = """
                using System;
                readonly ref struct R1
                {
                    public void M(ReadOnlySpan<int?> s) { s.Report(); }
                    public object this[ReadOnlySpan<int?> s] { get { s.Report(); return null; } }
                }
                ref struct R2
                {
                    public readonly void M(ReadOnlySpan<int?> s) { s.Report(); }
                    public readonly object this[ReadOnlySpan<int?> s] { get { s.Report(); return null; } }
                }
                class Program
                {
                    static void Main()
                    {
                        var r1 = new R1();
                        r1.M([3]);
                        _ = r1[[4]];
                        var r2 = new R2();
                        r2.M([5]);
                        _ = r2[[6]];
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[3], [4], [5], [6], "));
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size      187 (0xbb)
                  .maxstack  3
                  .locals init (R1 V_0, //r1
                                R2 V_1, //r2
                                <>y__InlineArray1<int?> V_2,
                                <>y__InlineArray1<int?> V_3,
                                <>y__InlineArray1<int?> V_4,
                                <>y__InlineArray1<int?> V_5)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "R1"
                  IL_0008:  ldloca.s   V_0
                  IL_000a:  ldloca.s   V_2
                  IL_000c:  initobj    "<>y__InlineArray1<int?>"
                  IL_0012:  ldloca.s   V_2
                  IL_0014:  ldc.i4.0
                  IL_0015:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_001a:  ldc.i4.3
                  IL_001b:  newobj     "int?..ctor(int)"
                  IL_0020:  stobj      "int?"
                  IL_0025:  ldloca.s   V_2
                  IL_0027:  ldc.i4.1
                  IL_0028:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_002d:  call       "void R1.M(System.ReadOnlySpan<int?>)"
                  IL_0032:  ldloca.s   V_0
                  IL_0034:  ldloca.s   V_3
                  IL_0036:  initobj    "<>y__InlineArray1<int?>"
                  IL_003c:  ldloca.s   V_3
                  IL_003e:  ldc.i4.0
                  IL_003f:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_0044:  ldc.i4.4
                  IL_0045:  newobj     "int?..ctor(int)"
                  IL_004a:  stobj      "int?"
                  IL_004f:  ldloca.s   V_3
                  IL_0051:  ldc.i4.1
                  IL_0052:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_0057:  call       "object R1.this[System.ReadOnlySpan<int?>].get"
                  IL_005c:  pop
                  IL_005d:  ldloca.s   V_1
                  IL_005f:  initobj    "R2"
                  IL_0065:  ldloca.s   V_1
                  IL_0067:  ldloca.s   V_4
                  IL_0069:  initobj    "<>y__InlineArray1<int?>"
                  IL_006f:  ldloca.s   V_4
                  IL_0071:  ldc.i4.0
                  IL_0072:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_0077:  ldc.i4.5
                  IL_0078:  newobj     "int?..ctor(int)"
                  IL_007d:  stobj      "int?"
                  IL_0082:  ldloca.s   V_4
                  IL_0084:  ldc.i4.1
                  IL_0085:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_008a:  call       "readonly void R2.M(System.ReadOnlySpan<int?>)"
                  IL_008f:  ldloca.s   V_1
                  IL_0091:  ldloca.s   V_5
                  IL_0093:  initobj    "<>y__InlineArray1<int?>"
                  IL_0099:  ldloca.s   V_5
                  IL_009b:  ldc.i4.0
                  IL_009c:  call       "InlineArrayElementRef<<>y__InlineArray1<int?>, int?>(ref <>y__InlineArray1<int?>, int)"
                  IL_00a1:  ldc.i4.6
                  IL_00a2:  newobj     "int?..ctor(int)"
                  IL_00a7:  stobj      "int?"
                  IL_00ac:  ldloca.s   V_5
                  IL_00ae:  ldc.i4.1
                  IL_00af:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int?>, int?>(in <>y__InlineArray1<int?>, int)"
                  IL_00b4:  call       "readonly object R2.this[System.ReadOnlySpan<int?>].get"
                  IL_00b9:  pop
                  IL_00ba:  ret
                }
                """);
        }

        [Fact]
        public void SpanArgument_Nested()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F1([F1([1]) + 2]);
                        F2([F2([2]) + 2]);
                    }
                    static T F1<T>(Span<T> s) { s.Report(); return s[0]; }
                    static T F2<T>(ReadOnlySpan<T> s) { s.Report(); return s[0]; }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [3], [2], [4], "));
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size      113 (0x71)
                  .maxstack  3
                  .locals init (<>y__InlineArray1<int> V_0,
                                <>y__InlineArray1<int> V_1,
                                <>y__InlineArray1<int> V_2)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "<>y__InlineArray1<int>"
                  IL_0008:  ldloca.s   V_0
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<int>, int>(ref <>y__InlineArray1<int>, int)"
                  IL_0010:  ldloca.s   V_1
                  IL_0012:  initobj    "<>y__InlineArray1<int>"
                  IL_0018:  ldloca.s   V_1
                  IL_001a:  ldc.i4.0
                  IL_001b:  call       "InlineArrayElementRef<<>y__InlineArray1<int>, int>(ref <>y__InlineArray1<int>, int)"
                  IL_0020:  ldc.i4.1
                  IL_0021:  stind.i4
                  IL_0022:  ldloca.s   V_1
                  IL_0024:  ldc.i4.1
                  IL_0025:  call       "InlineArrayAsSpan<<>y__InlineArray1<int>, int>(ref <>y__InlineArray1<int>, int)"
                  IL_002a:  call       "int Program.F1<int>(System.Span<int>)"
                  IL_002f:  ldc.i4.2
                  IL_0030:  add
                  IL_0031:  stind.i4
                  IL_0032:  ldloca.s   V_0
                  IL_0034:  ldc.i4.1
                  IL_0035:  call       "InlineArrayAsSpan<<>y__InlineArray1<int>, int>(ref <>y__InlineArray1<int>, int)"
                  IL_003a:  call       "int Program.F1<int>(System.Span<int>)"
                  IL_003f:  pop
                  IL_0040:  ldloca.s   V_2
                  IL_0042:  initobj    "<>y__InlineArray1<int>"
                  IL_0048:  ldloca.s   V_2
                  IL_004a:  ldc.i4.0
                  IL_004b:  call       "InlineArrayElementRef<<>y__InlineArray1<int>, int>(ref <>y__InlineArray1<int>, int)"
                  IL_0050:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=4_Align=4 <PrivateImplementationDetails>.26B25D457597A7B0463F9620F666DD10AA2C4373A505967C7C8D70922A2D6ECE4"
                  IL_0055:  call       "System.ReadOnlySpan<int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<int>(System.RuntimeFieldHandle)"
                  IL_005a:  call       "int Program.F2<int>(System.ReadOnlySpan<int>)"
                  IL_005f:  ldc.i4.2
                  IL_0060:  add
                  IL_0061:  stind.i4
                  IL_0062:  ldloca.s   V_2
                  IL_0064:  ldc.i4.1
                  IL_0065:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<int>, int>(in <>y__InlineArray1<int>, int)"
                  IL_006a:  call       "int Program.F2<int>(System.ReadOnlySpan<int>)"
                  IL_006f:  pop
                  IL_0070:  ret
                }
                """);
        }

        [Fact]
        public void SpanArgument_Reordered()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F1<object>(y: [1], x: [2]);
                        F2<object>(y: [3], x: [4]);
                    }
                    static Span<T> F1<T>(Span<T> x, scoped Span<T> y)
                    {
                        x.Report();
                        y.Report();
                        return x;
                    }
                    static ReadOnlySpan<T> F2<T>(scoped ReadOnlySpan<T> x, ReadOnlySpan<T> y)
                    {
                        x.Report();
                        y.Report();
                        return y;
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[2], [1], [4], [3], "));
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size      145 (0x91)
                  .maxstack  2
                  .locals init (<>y__InlineArray1<object> V_0,
                                <>y__InlineArray1<object> V_1,
                                <>y__InlineArray1<object> V_2,
                                <>y__InlineArray1<object> V_3,
                                System.Span<object> V_4,
                                System.ReadOnlySpan<object> V_5)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "<>y__InlineArray1<object>"
                  IL_0008:  ldloca.s   V_0
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0010:  ldc.i4.1
                  IL_0011:  box        "int"
                  IL_0016:  stind.ref
                  IL_0017:  ldloca.s   V_0
                  IL_0019:  ldc.i4.1
                  IL_001a:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_001f:  stloc.s    V_4
                  IL_0021:  ldloca.s   V_1
                  IL_0023:  initobj    "<>y__InlineArray1<object>"
                  IL_0029:  ldloca.s   V_1
                  IL_002b:  ldc.i4.0
                  IL_002c:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0031:  ldc.i4.2
                  IL_0032:  box        "int"
                  IL_0037:  stind.ref
                  IL_0038:  ldloca.s   V_1
                  IL_003a:  ldc.i4.1
                  IL_003b:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0040:  ldloc.s    V_4
                  IL_0042:  call       "System.Span<object> Program.F1<object>(System.Span<object>, scoped System.Span<object>)"
                  IL_0047:  pop
                  IL_0048:  ldloca.s   V_2
                  IL_004a:  initobj    "<>y__InlineArray1<object>"
                  IL_0050:  ldloca.s   V_2
                  IL_0052:  ldc.i4.0
                  IL_0053:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0058:  ldc.i4.3
                  IL_0059:  box        "int"
                  IL_005e:  stind.ref
                  IL_005f:  ldloca.s   V_2
                  IL_0061:  ldc.i4.1
                  IL_0062:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                  IL_0067:  stloc.s    V_5
                  IL_0069:  ldloca.s   V_3
                  IL_006b:  initobj    "<>y__InlineArray1<object>"
                  IL_0071:  ldloca.s   V_3
                  IL_0073:  ldc.i4.0
                  IL_0074:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0079:  ldc.i4.4
                  IL_007a:  box        "int"
                  IL_007f:  stind.ref
                  IL_0080:  ldloca.s   V_3
                  IL_0082:  ldc.i4.1
                  IL_0083:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                  IL_0088:  ldloc.s    V_5
                  IL_008a:  call       "System.ReadOnlySpan<object> Program.F2<object>(scoped System.ReadOnlySpan<object>, System.ReadOnlySpan<object>)"
                  IL_008f:  pop
                  IL_0090:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void SpanArgument_Constructor_01(
            [CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework,
            bool useScoped)
        {
            string source = $$"""
                using System;
                ref struct R<T>
                {
                    public R(T x, T y, T z) : this([x, y, z])
                    {
                    }
                    public R(int x, T[] y) : this([..y])
                    {
                    }
                    public R({{(useScoped ? "scoped" : "")}} Span<T> s)
                    {
                        F = s.ToArray();
                    }
                    public readonly T[] F;
                }
                class Program
                {
                    static void Main()
                    {
                        R<int> x = new R<int>(1, 2, 3);
                        R<object> y = new R<object>(new object[] { 4, 5 });
                        x.F.Report();
                        y.F.Report();
                    }
                }
                """;
            var comp = CreateCompilation(
                new[] { source, s_collectionExtensions },
                targetFramework: targetFramework,
                options: TestOptions.ReleaseExe);
            if (!useScoped)
            {
                comp.VerifyEmitDiagnostics(
                    // 0.cs(4,29): error CS8350: This combination of arguments to 'R<T>.R(Span<T>)' is disallowed because it may expose variables referenced by parameter 's' outside of their declaration scope
                    //     public R(T x, T y, T z) : this([x, y, z])
                    Diagnostic(ErrorCode.ERR_CallArgMixing, ": this([x, y, z])").WithArguments("R<T>.R(System.Span<T>)", "s").WithLocation(4, 29),
                    // 0.cs(4,36): error CS9203: A collection expression of type 'Span<T>' cannot be used in this context because it may be exposed outside of the current scope.
                    //     public R(T x, T y, T z) : this([x, y, z])
                    Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[x, y, z]").WithArguments("System.Span<T>").WithLocation(4, 36),
                    // 0.cs(7,28): error CS8350: This combination of arguments to 'R<T>.R(Span<T>)' is disallowed because it may expose variables referenced by parameter 's' outside of their declaration scope
                    //     public R(int x, T[] y) : this([..y])
                    Diagnostic(ErrorCode.ERR_CallArgMixing, ": this([..y])").WithArguments("R<T>.R(System.Span<T>)", "s").WithLocation(7, 28),
                    // 0.cs(7,35): error CS9203: A collection expression of type 'Span<T>' cannot be used in this context because it may be exposed outside of the current scope.
                    //     public R(int x, T[] y) : this([..y])
                    Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[..y]").WithArguments("System.Span<T>").WithLocation(7, 35));
            }
            else if (targetFramework == TargetFramework.Net80)
            {
                var verifier = CompileAndVerify(
                    comp,
                    verify: Verification.Skipped,
                    expectedOutput: IncludeExpectedOutput("[1, 2, 3], [4, 5], "));
                verifier.VerifyIL("R<T>..ctor(T, T, T)", """
                    {
                      // Code size       65 (0x41)
                      .maxstack  3
                      .locals init (<>y__InlineArray3<T> V_0)
                      IL_0000:  ldarg.0
                      IL_0001:  ldloca.s   V_0
                      IL_0003:  initobj    "<>y__InlineArray3<T>"
                      IL_0009:  ldloca.s   V_0
                      IL_000b:  ldc.i4.0
                      IL_000c:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                      IL_0011:  ldarg.1
                      IL_0012:  stobj      "T"
                      IL_0017:  ldloca.s   V_0
                      IL_0019:  ldc.i4.1
                      IL_001a:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                      IL_001f:  ldarg.2
                      IL_0020:  stobj      "T"
                      IL_0025:  ldloca.s   V_0
                      IL_0027:  ldc.i4.2
                      IL_0028:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                      IL_002d:  ldarg.3
                      IL_002e:  stobj      "T"
                      IL_0033:  ldloca.s   V_0
                      IL_0035:  ldc.i4.3
                      IL_0036:  call       "InlineArrayAsSpan<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                      IL_003b:  call       "R<T>..ctor(scoped System.Span<T>)"
                      IL_0040:  ret
                    }
                    """);
                verifier.VerifyIL("R<T>..ctor(int, T[])", """
                    {
                      // Code size       55 (0x37)
                      .maxstack  2
                      .locals init (System.Collections.Generic.List<T> V_0,
                                    T[] V_1,
                                    int V_2,
                                    T V_3)
                      IL_0000:  newobj     "System.Collections.Generic.List<T>..ctor()"
                      IL_0005:  stloc.0
                      IL_0006:  ldarg.2
                      IL_0007:  stloc.1
                      IL_0008:  ldc.i4.0
                      IL_0009:  stloc.2
                      IL_000a:  br.s       IL_001f
                      IL_000c:  ldloc.1
                      IL_000d:  ldloc.2
                      IL_000e:  ldelem     "T"
                      IL_0013:  stloc.3
                      IL_0014:  ldloc.0
                      IL_0015:  ldloc.3
                      IL_0016:  callvirt   "void System.Collections.Generic.List<T>.Add(T)"
                      IL_001b:  ldloc.2
                      IL_001c:  ldc.i4.1
                      IL_001d:  add
                      IL_001e:  stloc.2
                      IL_001f:  ldloc.2
                      IL_0020:  ldloc.1
                      IL_0021:  ldlen
                      IL_0022:  conv.i4
                      IL_0023:  blt.s      IL_000c
                      IL_0025:  ldarg.0
                      IL_0026:  ldloc.0
                      IL_0027:  callvirt   "T[] System.Collections.Generic.List<T>.ToArray()"
                      IL_002c:  newobj     "System.Span<T>..ctor(T[])"
                      IL_0031:  call       "R<T>..ctor(scoped System.Span<T>)"
                      IL_0036:  ret
                    }
                    """);
            }
        }

        [Fact]
        public void SpanArgument_Constructor_02()
        {
            string source = """
                using System;
                record class A<T>(T[] F)
                {
                    public static T[] ToArray(ReadOnlySpan<T> s) => s.ToArray();
                }
                record class B<T>(T x, T y, T z) : A<T>(ToArray([x, y, z]));
                class Program
                {
                    static void Main()
                    {
                        object[] a = F<object>(1, 2, 3);
                        a.Report();
                    }
                    static T[] F<T>(T x, T y, T z)
                    {
                        B<T> b = new B<T>(x, y, z);
                        return b.F;
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("B<T>..ctor(T, T, T)", """
                {
                  // Code size       91 (0x5b)
                  .maxstack  3
                  .locals init (<>y__InlineArray3<T> V_0)
                  IL_0000:  ldarg.0
                  IL_0001:  ldarg.1
                  IL_0002:  stfld      "T B<T>.<x>k__BackingField"
                  IL_0007:  ldarg.0
                  IL_0008:  ldarg.2
                  IL_0009:  stfld      "T B<T>.<y>k__BackingField"
                  IL_000e:  ldarg.0
                  IL_000f:  ldarg.3
                  IL_0010:  stfld      "T B<T>.<z>k__BackingField"
                  IL_0015:  ldarg.0
                  IL_0016:  ldloca.s   V_0
                  IL_0018:  initobj    "<>y__InlineArray3<T>"
                  IL_001e:  ldloca.s   V_0
                  IL_0020:  ldc.i4.0
                  IL_0021:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0026:  ldarg.1
                  IL_0027:  stobj      "T"
                  IL_002c:  ldloca.s   V_0
                  IL_002e:  ldc.i4.1
                  IL_002f:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0034:  ldarg.2
                  IL_0035:  stobj      "T"
                  IL_003a:  ldloca.s   V_0
                  IL_003c:  ldc.i4.2
                  IL_003d:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0042:  ldarg.3
                  IL_0043:  stobj      "T"
                  IL_0048:  ldloca.s   V_0
                  IL_004a:  ldc.i4.3
                  IL_004b:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<T>, T>(in <>y__InlineArray3<T>, int)"
                  IL_0050:  call       "T[] A<T>.ToArray(System.ReadOnlySpan<T>)"
                  IL_0055:  call       "A<T>..ctor(T[])"
                  IL_005a:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void SpanAssignment_01(
            [CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework,
            [CombinatorialValues("Span<object>", "ReadOnlySpan<object>")] string spanType)
        {
            string source = $$"""
                using System;
                class Program
                {
                    static {{spanType}} F1()
                    {
                        {{spanType}} s1 = [];
                        return s1;
                    }
                    static {{spanType}} F2()
                    {
                        {{spanType}} s2 = [2];
                        return s2;
                    }
                    static {{spanType}} F3()
                    {
                        {{spanType}} s3;
                        s3 = [3];
                        return s3;
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: targetFramework);
            comp.VerifyEmitDiagnostics(
                // (12,16): error CS8352: Cannot use variable 's2' in this context because it may expose referenced variables outside of their declaration scope
                //         return s2;
                Diagnostic(ErrorCode.ERR_EscapeVariable, "s2").WithArguments("s2").WithLocation(12, 16),
                // (17,14): error CS9203: A collection expression of type 'Span<object>' cannot be used in this context because it may be exposed outside of the current scope.
                //         s3 = [3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[3]").WithArguments($"System.{spanType}").WithLocation(17, 14));
        }

        [Fact]
        public void SpanAssignment_02()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F1().Report();
                        F2().Report();
                    }
                    static object[] F1()
                    {
                        Span<object> s1 = [1];
                        return s1.ToArray();
                    }
                    static object[] F2()
                    {
                        ReadOnlySpan<object> s2 = [2];
                        return s2.ToArray();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [2], "));
            verifier.VerifyIL("Program.F1", """
                {
                    // Code size       40 (0x28)
                    .maxstack  2
                    .locals init (System.Span<object> V_0, //s1
                                <>y__InlineArray1<object> V_1)
                    IL_0000:  ldloca.s   V_1
                    IL_0002:  initobj    "<>y__InlineArray1<object>"
                    IL_0008:  ldloca.s   V_1
                    IL_000a:  ldc.i4.0
                    IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0010:  ldc.i4.1
                    IL_0011:  box        "int"
                    IL_0016:  stind.ref
                    IL_0017:  ldloca.s   V_1
                    IL_0019:  ldc.i4.1
                    IL_001a:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_001f:  stloc.0
                    IL_0020:  ldloca.s   V_0
                    IL_0022:  call       "object[] System.Span<object>.ToArray()"
                    IL_0027:  ret
                }
                """);
            verifier.VerifyIL("Program.F2", """
                {
                    // Code size       40 (0x28)
                    .maxstack  2
                    .locals init (System.ReadOnlySpan<object> V_0, //s2
                                <>y__InlineArray1<object> V_1)
                    IL_0000:  ldloca.s   V_1
                    IL_0002:  initobj    "<>y__InlineArray1<object>"
                    IL_0008:  ldloca.s   V_1
                    IL_000a:  ldc.i4.0
                    IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0010:  ldc.i4.2
                    IL_0011:  box        "int"
                    IL_0016:  stind.ref
                    IL_0017:  ldloca.s   V_1
                    IL_0019:  ldc.i4.1
                    IL_001a:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                    IL_001f:  stloc.0
                    IL_0020:  ldloca.s   V_0
                    IL_0022:  call       "object[] System.ReadOnlySpan<object>.ToArray()"
                    IL_0027:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void SpanAssignment_03([CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        scoped Span<object> x;
                        scoped ReadOnlySpan<object> y;
                        x = [1];
                        y = [2];
                        x.Report();
                        y.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: targetFramework,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [2], "));
            if (targetFramework == TargetFramework.Net80)
            {
                verifier.VerifyIL("Program.Main", """
                    {
                      // Code size       79 (0x4f)
                      .maxstack  2
                      .locals init (System.Span<object> V_0, //x
                                    System.ReadOnlySpan<object> V_1, //y
                                    <>y__InlineArray1<object> V_2,
                                    <>y__InlineArray1<object> V_3)
                      IL_0000:  ldloca.s   V_2
                      IL_0002:  initobj    "<>y__InlineArray1<object>"
                      IL_0008:  ldloca.s   V_2
                      IL_000a:  ldc.i4.0
                      IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0010:  ldc.i4.1
                      IL_0011:  box        "int"
                      IL_0016:  stind.ref
                      IL_0017:  ldloca.s   V_2
                      IL_0019:  ldc.i4.1
                      IL_001a:  call       "InlineArrayAsSpan<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_001f:  stloc.0
                      IL_0020:  ldloca.s   V_3
                      IL_0022:  initobj    "<>y__InlineArray1<object>"
                      IL_0028:  ldloca.s   V_3
                      IL_002a:  ldc.i4.0
                      IL_002b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0030:  ldc.i4.2
                      IL_0031:  box        "int"
                      IL_0036:  stind.ref
                      IL_0037:  ldloca.s   V_3
                      IL_0039:  ldc.i4.1
                      IL_003a:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                      IL_003f:  stloc.1
                      IL_0040:  ldloca.s   V_0
                      IL_0042:  call       "void CollectionExtensions.Report<object>(in System.Span<object>)"
                      IL_0047:  ldloca.s   V_1
                      IL_0049:  call       "void CollectionExtensions.Report<object>(in System.ReadOnlySpan<object>)"
                      IL_004e:  ret
                    }
                    """);
            }
            else
            {
                verifier.VerifyIL("Program.Main", """
                    {
                      // Code size       59 (0x3b)
                      .maxstack  5
                      .locals init (System.Span<object> V_0, //x
                                    System.ReadOnlySpan<object> V_1) //y
                      IL_0000:  ldloca.s   V_0
                      IL_0002:  ldc.i4.1
                      IL_0003:  newarr     "object"
                      IL_0008:  dup
                      IL_0009:  ldc.i4.0
                      IL_000a:  ldc.i4.1
                      IL_000b:  box        "int"
                      IL_0010:  stelem.ref
                      IL_0011:  call       "System.Span<object>..ctor(object[])"
                      IL_0016:  ldloca.s   V_1
                      IL_0018:  ldc.i4.1
                      IL_0019:  newarr     "object"
                      IL_001e:  dup
                      IL_001f:  ldc.i4.0
                      IL_0020:  ldc.i4.2
                      IL_0021:  box        "int"
                      IL_0026:  stelem.ref
                      IL_0027:  call       "System.ReadOnlySpan<object>..ctor(object[])"
                      IL_002c:  ldloca.s   V_0
                      IL_002e:  call       "void CollectionExtensions.Report<object>(in System.Span<object>)"
                      IL_0033:  ldloca.s   V_1
                      IL_0035:  call       "void CollectionExtensions.Report<object>(in System.ReadOnlySpan<object>)"
                      IL_003a:  ret
                    }
                    """);
            }
        }

        [Fact]
        public void SpanAssignment_Field_01()
        {
            string source = """
                using System;
                ref struct R<T>
                {
                    public ReadOnlySpan<T> F;
                }
                class Program
                {
                    static void Main()
                    {
                        R<object> r = default;
                        r.F = [1];
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (11,15): error CS9203: A collection expression of type 'ReadOnlySpan<object>' cannot be used in this context because it may be exposed outside of the current scope.
                //         r.F = [1];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[1]").WithArguments("System.ReadOnlySpan<object>").WithLocation(11, 15));
        }

        [Fact]
        public void SpanAssignment_Field_02()
        {
            string source = """
                using System;
                ref struct R<T>
                {
                    public ReadOnlySpan<T> F;
                }
                class Program
                {
                    static void Main()
                    {
                        scoped R<object> x = default;
                        scoped R<object> y = default;
                        x.F = [1];
                        y.F = [2];
                        x.F.Report();
                        y.F.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [2], "));
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size      117 (0x75)
                  .maxstack  3
                  .locals init (R<object> V_0, //x
                                R<object> V_1, //y
                                <>y__InlineArray1<object> V_2,
                                <>y__InlineArray1<object> V_3)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "R<object>"
                  IL_0008:  ldloca.s   V_1
                  IL_000a:  initobj    "R<object>"
                  IL_0010:  ldloca.s   V_0
                  IL_0012:  ldloca.s   V_2
                  IL_0014:  initobj    "<>y__InlineArray1<object>"
                  IL_001a:  ldloca.s   V_2
                  IL_001c:  ldc.i4.0
                  IL_001d:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0022:  ldc.i4.1
                  IL_0023:  box        "int"
                  IL_0028:  stind.ref
                  IL_0029:  ldloca.s   V_2
                  IL_002b:  ldc.i4.1
                  IL_002c:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                  IL_0031:  stfld      "System.ReadOnlySpan<object> R<object>.F"
                  IL_0036:  ldloca.s   V_1
                  IL_0038:  ldloca.s   V_3
                  IL_003a:  initobj    "<>y__InlineArray1<object>"
                  IL_0040:  ldloca.s   V_3
                  IL_0042:  ldc.i4.0
                  IL_0043:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0048:  ldc.i4.2
                  IL_0049:  box        "int"
                  IL_004e:  stind.ref
                  IL_004f:  ldloca.s   V_3
                  IL_0051:  ldc.i4.1
                  IL_0052:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                  IL_0057:  stfld      "System.ReadOnlySpan<object> R<object>.F"
                  IL_005c:  ldloca.s   V_0
                  IL_005e:  ldflda     "System.ReadOnlySpan<object> R<object>.F"
                  IL_0063:  call       "void CollectionExtensions.Report<object>(in System.ReadOnlySpan<object>)"
                  IL_0068:  ldloca.s   V_1
                  IL_006a:  ldflda     "System.ReadOnlySpan<object> R<object>.F"
                  IL_006f:  call       "void CollectionExtensions.Report<object>(in System.ReadOnlySpan<object>)"
                  IL_0074:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_FieldInitializer_01()
        {
            string source = """
                using System;
                ref struct R
                {
                    public ReadOnlySpan<object> F = [1, 2, 3];
                    public R() { }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (4,37): error CS9203: A collection expression of type 'ReadOnlySpan<object>' cannot be used in this context because it may be exposed outside of the current scope.
                //     public ReadOnlySpan<object> F = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[1, 2, 3]").WithArguments("System.ReadOnlySpan<object>").WithLocation(4, 37));
        }

        [Fact]
        public void SpanAssignment_FieldInitializer_02()
        {
            string source = """
                using System;
                class Program
                {
                    static T[] FromSpan<T>(Span<T> s) => s.ToArray();
                    static int[] F = FromSpan([1, 2, 3]);
                    static void Main()
                    {
                        F.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("Program..cctor", """
                {
                  // Code size       57 (0x39)
                  .maxstack  2
                  .locals init (<>y__InlineArray3<int> V_0)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "<>y__InlineArray3<int>"
                  IL_0008:  ldloca.s   V_0
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_0010:  ldc.i4.1
                  IL_0011:  stind.i4
                  IL_0012:  ldloca.s   V_0
                  IL_0014:  ldc.i4.1
                  IL_0015:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_001a:  ldc.i4.2
                  IL_001b:  stind.i4
                  IL_001c:  ldloca.s   V_0
                  IL_001e:  ldc.i4.2
                  IL_001f:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_0024:  ldc.i4.3
                  IL_0025:  stind.i4
                  IL_0026:  ldloca.s   V_0
                  IL_0028:  ldc.i4.3
                  IL_0029:  call       "InlineArrayAsSpan<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_002e:  call       "int[] Program.FromSpan<int>(System.Span<int>)"
                  IL_0033:  stsfld     "int[] Program.F"
                  IL_0038:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_FieldInitializer_03()
        {
            string source = """
                using System;
                class C
                {
                    static T[] FromSpan<T>(ReadOnlySpan<T> s) => s.ToArray();
                    public object[] F = FromSpan<object>([1, 2, 3]);
                }
                class Program
                {
                    static void Main()
                    {
                        (new C()).F.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("C..ctor", """
                {
                  // Code size       79 (0x4f)
                  .maxstack  3
                  .locals init (<>y__InlineArray3<object> V_0)
                  IL_0000:  ldarg.0
                  IL_0001:  ldloca.s   V_0
                  IL_0003:  initobj    "<>y__InlineArray3<object>"
                  IL_0009:  ldloca.s   V_0
                  IL_000b:  ldc.i4.0
                  IL_000c:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_0011:  ldc.i4.1
                  IL_0012:  box        "int"
                  IL_0017:  stind.ref
                  IL_0018:  ldloca.s   V_0
                  IL_001a:  ldc.i4.1
                  IL_001b:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_0020:  ldc.i4.2
                  IL_0021:  box        "int"
                  IL_0026:  stind.ref
                  IL_0027:  ldloca.s   V_0
                  IL_0029:  ldc.i4.2
                  IL_002a:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_002f:  ldc.i4.3
                  IL_0030:  box        "int"
                  IL_0035:  stind.ref
                  IL_0036:  ldloca.s   V_0
                  IL_0038:  ldc.i4.3
                  IL_0039:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<object>, object>(in <>y__InlineArray3<object>, int)"
                  IL_003e:  call       "object[] C.FromSpan<object>(System.ReadOnlySpan<object>)"
                  IL_0043:  stfld      "object[] C.F"
                  IL_0048:  ldarg.0
                  IL_0049:  call       "object..ctor()"
                  IL_004e:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_FieldInitializer_04()
        {
            string source = """
                using System;
                struct S
                {
                    static T[] FromSpan<T>(ReadOnlySpan<T> s) => s.ToArray();
                    public object[] F = FromSpan<object>([1, 2, 3]);
                    public S() { }
                }
                class Program
                {
                    static void Main()
                    {
                        (new S()).F.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("S..ctor", """
                {
                  // Code size       73 (0x49)
                  .maxstack  3
                  .locals init (<>y__InlineArray3<object> V_0)
                  IL_0000:  ldarg.0
                  IL_0001:  ldloca.s   V_0
                  IL_0003:  initobj    "<>y__InlineArray3<object>"
                  IL_0009:  ldloca.s   V_0
                  IL_000b:  ldc.i4.0
                  IL_000c:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_0011:  ldc.i4.1
                  IL_0012:  box        "int"
                  IL_0017:  stind.ref
                  IL_0018:  ldloca.s   V_0
                  IL_001a:  ldc.i4.1
                  IL_001b:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_0020:  ldc.i4.2
                  IL_0021:  box        "int"
                  IL_0026:  stind.ref
                  IL_0027:  ldloca.s   V_0
                  IL_0029:  ldc.i4.2
                  IL_002a:  call       "InlineArrayElementRef<<>y__InlineArray3<object>, object>(ref <>y__InlineArray3<object>, int)"
                  IL_002f:  ldc.i4.3
                  IL_0030:  box        "int"
                  IL_0035:  stind.ref
                  IL_0036:  ldloca.s   V_0
                  IL_0038:  ldc.i4.3
                  IL_0039:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<object>, object>(in <>y__InlineArray3<object>, int)"
                  IL_003e:  call       "object[] S.FromSpan<object>(System.ReadOnlySpan<object>)"
                  IL_0043:  stfld      "object[] S.F"
                  IL_0048:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void SpanAssignment_RefLocal([CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class Program
                {
                    static Span<object> F()
                    {
                        Span<object> s = default;
                        ref Span<object> r = ref s;
                        r = new Span<object>(new object[] { 1 });
                        r = [1];
                        return r;
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: targetFramework);
            comp.VerifyEmitDiagnostics(
                // (9,13): error CS9203: A collection expression of type 'Span<object>' cannot be used in this context because it may be exposed outside of the current scope.
                //         r = [1];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[1]").WithArguments("System.Span<object>").WithLocation(9, 13));
        }

        [CombinatorialData]
        [Theory]
        public void SpanAssignment_NestedScope_01([CombinatorialValues(TargetFramework.Net70, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F(false);
                        F(true);
                    }
                    static void F(bool b)
                    {
                        ReadOnlySpan<object> x = [1];
                        if (b)
                        {
                            x = [2];
                        }
                        else
                        {
                            ReadOnlySpan<object> y = [3];
                            x = y;
                        }
                        ReadOnlySpan<object> z = [4];
                        x = z;
                    }
                }
                """;
            var verifier = CompileAndVerify(
                source,
                targetFramework: targetFramework,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput(""));
            if (targetFramework == TargetFramework.Net80)
            {
                verifier.VerifyIL("Program.F", """
                    {
                      // Code size      134 (0x86)
                      .maxstack  2
                      .locals init (<>y__InlineArray1<object> V_0,
                                    <>y__InlineArray1<object> V_1,
                                    <>y__InlineArray1<object> V_2,
                                    <>y__InlineArray1<object> V_3)
                      IL_0000:  ldloca.s   V_0
                      IL_0002:  initobj    "<>y__InlineArray1<object>"
                      IL_0008:  ldloca.s   V_0
                      IL_000a:  ldc.i4.0
                      IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0010:  ldc.i4.1
                      IL_0011:  box        "int"
                      IL_0016:  stind.ref
                      IL_0017:  ldloca.s   V_0
                      IL_0019:  ldc.i4.1
                      IL_001a:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                      IL_001f:  pop
                      IL_0020:  ldarg.0
                      IL_0021:  brfalse.s  IL_0045
                      IL_0023:  ldloca.s   V_1
                      IL_0025:  initobj    "<>y__InlineArray1<object>"
                      IL_002b:  ldloca.s   V_1
                      IL_002d:  ldc.i4.0
                      IL_002e:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0033:  ldc.i4.2
                      IL_0034:  box        "int"
                      IL_0039:  stind.ref
                      IL_003a:  ldloca.s   V_1
                      IL_003c:  ldc.i4.1
                      IL_003d:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                      IL_0042:  pop
                      IL_0043:  br.s       IL_0065
                      IL_0045:  ldloca.s   V_2
                      IL_0047:  initobj    "<>y__InlineArray1<object>"
                      IL_004d:  ldloca.s   V_2
                      IL_004f:  ldc.i4.0
                      IL_0050:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0055:  ldc.i4.3
                      IL_0056:  box        "int"
                      IL_005b:  stind.ref
                      IL_005c:  ldloca.s   V_2
                      IL_005e:  ldc.i4.1
                      IL_005f:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                      IL_0064:  pop
                      IL_0065:  ldloca.s   V_3
                      IL_0067:  initobj    "<>y__InlineArray1<object>"
                      IL_006d:  ldloca.s   V_3
                      IL_006f:  ldc.i4.0
                      IL_0070:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                      IL_0075:  ldc.i4.4
                      IL_0076:  box        "int"
                      IL_007b:  stind.ref
                      IL_007c:  ldloca.s   V_3
                      IL_007e:  ldc.i4.1
                      IL_007f:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                      IL_0084:  pop
                      IL_0085:  ret
                    }
                    """);
            }
        }

        [Fact]
        public void SpanAssignment_NestedScope_02()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        M(true, 1, 2, 3, 4);
                    }
                    static void M<T>(bool b, T x, T y, T z, T w)
                    {
                        scoped Span<T> s = default;
                        if (b)
                        {
                            s = [x, y, z];
                        }
                        if (b)
                        {
                            s = [z, w];
                        }
                        s.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[3, 4], "));
            verifier.VerifyIL("Program.M<T>", """
                {
                  // Code size      127 (0x7f)
                  .maxstack  2
                  .locals init (System.Span<T> V_0, //s
                                <>y__InlineArray3<T> V_1,
                                <>y__InlineArray2<T> V_2)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "System.Span<T>"
                  IL_0008:  ldarg.0
                  IL_0009:  brfalse.s  IL_0046
                  IL_000b:  ldloca.s   V_1
                  IL_000d:  initobj    "<>y__InlineArray3<T>"
                  IL_0013:  ldloca.s   V_1
                  IL_0015:  ldc.i4.0
                  IL_0016:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_001b:  ldarg.1
                  IL_001c:  stobj      "T"
                  IL_0021:  ldloca.s   V_1
                  IL_0023:  ldc.i4.1
                  IL_0024:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0029:  ldarg.2
                  IL_002a:  stobj      "T"
                  IL_002f:  ldloca.s   V_1
                  IL_0031:  ldc.i4.2
                  IL_0032:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0037:  ldarg.3
                  IL_0038:  stobj      "T"
                  IL_003d:  ldloca.s   V_1
                  IL_003f:  ldc.i4.3
                  IL_0040:  call       "InlineArrayAsSpan<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0045:  stloc.0
                  IL_0046:  ldarg.0
                  IL_0047:  brfalse.s  IL_0077
                  IL_0049:  ldloca.s   V_2
                  IL_004b:  initobj    "<>y__InlineArray2<T>"
                  IL_0051:  ldloca.s   V_2
                  IL_0053:  ldc.i4.0
                  IL_0054:  call       "InlineArrayElementRef<<>y__InlineArray2<T>, T>(ref <>y__InlineArray2<T>, int)"
                  IL_0059:  ldarg.3
                  IL_005a:  stobj      "T"
                  IL_005f:  ldloca.s   V_2
                  IL_0061:  ldc.i4.1
                  IL_0062:  call       "InlineArrayElementRef<<>y__InlineArray2<T>, T>(ref <>y__InlineArray2<T>, int)"
                  IL_0067:  ldarg.s    V_4
                  IL_0069:  stobj      "T"
                  IL_006e:  ldloca.s   V_2
                  IL_0070:  ldc.i4.2
                  IL_0071:  call       "InlineArrayAsSpan<<>y__InlineArray2<T>, T>(ref <>y__InlineArray2<T>, int)"
                  IL_0076:  stloc.0
                  IL_0077:  ldloca.s   V_0
                  IL_0079:  call       "void CollectionExtensions.Report<T>(in System.Span<T>)"
                  IL_007e:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_NestedScope_03()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        M<object>(true, [1, null, 3]);
                    }
                    static void M<T>(bool b, T[] a)
                    {
                        scoped Span<T> s = default;
                        if (b)
                        {
                            s = [..a];
                        }
                        s.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, null, 3], "));
            verifier.VerifyIL("Program.M<T>", """
                {
                  // Code size       71 (0x47)
                  .maxstack  2
                  .locals init (System.Span<T> V_0, //s
                                System.Collections.Generic.List<T> V_1,
                                T[] V_2,
                                int V_3,
                                T V_4)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "System.Span<T>"
                  IL_0008:  ldarg.0
                  IL_0009:  brfalse.s  IL_003f
                  IL_000b:  newobj     "System.Collections.Generic.List<T>..ctor()"
                  IL_0010:  stloc.1
                  IL_0011:  ldarg.1
                  IL_0012:  stloc.2
                  IL_0013:  ldc.i4.0
                  IL_0014:  stloc.3
                  IL_0015:  br.s       IL_002c
                  IL_0017:  ldloc.2
                  IL_0018:  ldloc.3
                  IL_0019:  ldelem     "T"
                  IL_001e:  stloc.s    V_4
                  IL_0020:  ldloc.1
                  IL_0021:  ldloc.s    V_4
                  IL_0023:  callvirt   "void System.Collections.Generic.List<T>.Add(T)"
                  IL_0028:  ldloc.3
                  IL_0029:  ldc.i4.1
                  IL_002a:  add
                  IL_002b:  stloc.3
                  IL_002c:  ldloc.3
                  IL_002d:  ldloc.2
                  IL_002e:  ldlen
                  IL_002f:  conv.i4
                  IL_0030:  blt.s      IL_0017
                  IL_0032:  ldloca.s   V_0
                  IL_0034:  ldloc.1
                  IL_0035:  callvirt   "T[] System.Collections.Generic.List<T>.ToArray()"
                  IL_003a:  call       "System.Span<T>..ctor(T[])"
                  IL_003f:  ldloca.s   V_0
                  IL_0041:  call       "void CollectionExtensions.Report<T>(in System.Span<T>)"
                  IL_0046:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_NestedScope_04()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        F<object>()(true, 1, null, 3);
                    }
                    static Action<bool, T, T, T> F<T>()
                    {
                        return (bool b, T x, T y, T z) =>
                            {
                                scoped Span<T> s1 = default;
                                if (b)
                                {
                                    Span<T> s2 = [x, y, z];
                                    s1 = s2;
                                }
                                s1.Report();
                            };
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, null, 3], "));
            verifier.VerifyIL("Program.<>c__1<T>.<F>b__1_0(bool, T, T, T)", """
                {
                  // Code size       79 (0x4f)
                  .maxstack  2
                  .locals init (System.Span<T> V_0, //s1
                                <>y__InlineArray3<T> V_1)
                  IL_0000:  ldloca.s   V_0
                  IL_0002:  initobj    "System.Span<T>"
                  IL_0008:  ldarg.1
                  IL_0009:  brfalse.s  IL_0047
                  IL_000b:  ldloca.s   V_1
                  IL_000d:  initobj    "<>y__InlineArray3<T>"
                  IL_0013:  ldloca.s   V_1
                  IL_0015:  ldc.i4.0
                  IL_0016:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_001b:  ldarg.2
                  IL_001c:  stobj      "T"
                  IL_0021:  ldloca.s   V_1
                  IL_0023:  ldc.i4.1
                  IL_0024:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0029:  ldarg.3
                  IL_002a:  stobj      "T"
                  IL_002f:  ldloca.s   V_1
                  IL_0031:  ldc.i4.2
                  IL_0032:  call       "InlineArrayElementRef<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0037:  ldarg.s    V_4
                  IL_0039:  stobj      "T"
                  IL_003e:  ldloca.s   V_1
                  IL_0040:  ldc.i4.3
                  IL_0041:  call       "InlineArrayAsSpan<<>y__InlineArray3<T>, T>(ref <>y__InlineArray3<T>, int)"
                  IL_0046:  stloc.0
                  IL_0047:  ldloca.s   V_0
                  IL_0049:  call       "void CollectionExtensions.Report<T>(in System.Span<T>)"
                  IL_004e:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_NestedScope_05()
        {
            string source = """
                using System;
                class Program
                {
                    static void Main()
                    {
                        M<object>(1, 2, 3, 4);
                    }
                    static void M<T>(T x, T y, T z, T w)
                    {
                        scoped Span<T> s1;
                        s1 = [x];
                        s1.Report();
                        Action a1 = () =>
                            {
                                scoped Span<T> s2;
                                s2 = [y];
                                s2.Report();
                                void A2()
                                {
                                    scoped Span<T> s3;
                                    s3 = [z];
                                    s3.Report();
                                }
                                A2();
                                s2 = [w];
                                s2.Report();
                            };
                        a1();
                        s1 = [x];
                        s1.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1], [2], [3], [4], [1], "));
            verifier.VerifyIL("Program.<>c__DisplayClass1_0<T>.<M>g__A2|1()", """
                {
                  // Code size       44 (0x2c)
                  .maxstack  2
                  .locals init (System.Span<T> V_0, //s3
                                <>y__InlineArray1<T> V_1)
                  IL_0000:  ldloca.s   V_1
                  IL_0002:  initobj    "<>y__InlineArray1<T>"
                  IL_0008:  ldloca.s   V_1
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<T>, T>(ref <>y__InlineArray1<T>, int)"
                  IL_0010:  ldarg.0
                  IL_0011:  ldfld      "T Program.<>c__DisplayClass1_0<T>.z"
                  IL_0016:  stobj      "T"
                  IL_001b:  ldloca.s   V_1
                  IL_001d:  ldc.i4.1
                  IL_001e:  call       "InlineArrayAsSpan<<>y__InlineArray1<T>, T>(ref <>y__InlineArray1<T>, int)"
                  IL_0023:  stloc.0
                  IL_0024:  ldloca.s   V_0
                  IL_0026:  call       "void CollectionExtensions.Report<T>(in System.Span<T>)"
                  IL_002b:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_NestedScope_06()
        {
            string source = """
                using System;
                class C<T>
                {
                    public Action<T, T> F = (T x, T y) =>
                        {
                            scoped ReadOnlySpan<T> r1;
                            Action<T> a = (T z) =>
                                {
                                    scoped ReadOnlySpan<T> r2;
                                    r2 = [z];
                                    r2.Report();
                                };
                            a(y);
                            r1 = [x];
                            r1.Report();
                        };
                }
                class Program
                {
                    static void Main()
                    {
                        var c = new C<string>();
                        c.F("a", "b");
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[b], [a], "));
            verifier.VerifyIL("C<T>.<>c.<.ctor>b__1_0(T, T)", """
                {
                  // Code size       76 (0x4c)
                  .maxstack  2
                  .locals init (System.ReadOnlySpan<T> V_0, //r1
                                <>y__InlineArray1<T> V_1)
                  IL_0000:  ldsfld     "System.Action<T> C<T>.<>c.<>9__1_1"
                  IL_0005:  dup
                  IL_0006:  brtrue.s   IL_001f
                  IL_0008:  pop
                  IL_0009:  ldsfld     "C<T>.<>c C<T>.<>c.<>9"
                  IL_000e:  ldftn      "void C<T>.<>c.<.ctor>b__1_1(T)"
                  IL_0014:  newobj     "System.Action<T>..ctor(object, nint)"
                  IL_0019:  dup
                  IL_001a:  stsfld     "System.Action<T> C<T>.<>c.<>9__1_1"
                  IL_001f:  ldarg.2
                  IL_0020:  callvirt   "void System.Action<T>.Invoke(T)"
                  IL_0025:  ldloca.s   V_1
                  IL_0027:  initobj    "<>y__InlineArray1<T>"
                  IL_002d:  ldloca.s   V_1
                  IL_002f:  ldc.i4.0
                  IL_0030:  call       "InlineArrayElementRef<<>y__InlineArray1<T>, T>(ref <>y__InlineArray1<T>, int)"
                  IL_0035:  ldarg.1
                  IL_0036:  stobj      "T"
                  IL_003b:  ldloca.s   V_1
                  IL_003d:  ldc.i4.1
                  IL_003e:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<T>, T>(in <>y__InlineArray1<T>, int)"
                  IL_0043:  stloc.0
                  IL_0044:  ldloca.s   V_0
                  IL_0046:  call       "void CollectionExtensions.Report<T>(in System.ReadOnlySpan<T>)"
                  IL_004b:  ret
                }
                """);
        }

        [Fact]
        public void SpanAssignment_WithUsingDeclaration()
        {
            string source = """
                using System;
                class Disposable : IDisposable
                {
                    void IDisposable.Dispose() { Console.Write("Disposed, "); }
                }
                class Program
                {
                    static void Main()
                    {
                        ReadOnlySpan<object> x = [1];
                        using var d = new Disposable();
                        ReadOnlySpan<object> y = [2];
                        x.Report();
                        y.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[1], [2], Disposed, "));
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size       97 (0x61)
                  .maxstack  2
                  .locals init (System.ReadOnlySpan<object> V_0, //x
                                Disposable V_1, //d
                                System.ReadOnlySpan<object> V_2, //y
                                <>y__InlineArray1<object> V_3,
                                <>y__InlineArray1<object> V_4)
                  IL_0000:  ldloca.s   V_3
                  IL_0002:  initobj    "<>y__InlineArray1<object>"
                  IL_0008:  ldloca.s   V_3
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                  IL_0010:  ldc.i4.1
                  IL_0011:  box        "int"
                  IL_0016:  stind.ref
                  IL_0017:  ldloca.s   V_3
                  IL_0019:  ldc.i4.1
                  IL_001a:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                  IL_001f:  stloc.0
                  IL_0020:  newobj     "Disposable..ctor()"
                  IL_0025:  stloc.1
                  .try
                  {
                    IL_0026:  ldloca.s   V_4
                    IL_0028:  initobj    "<>y__InlineArray1<object>"
                    IL_002e:  ldloca.s   V_4
                    IL_0030:  ldc.i4.0
                    IL_0031:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0036:  ldc.i4.2
                    IL_0037:  box        "int"
                    IL_003c:  stind.ref
                    IL_003d:  ldloca.s   V_4
                    IL_003f:  ldc.i4.1
                    IL_0040:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                    IL_0045:  stloc.2
                    IL_0046:  ldloca.s   V_0
                    IL_0048:  call       "void CollectionExtensions.Report<object>(in System.ReadOnlySpan<object>)"
                    IL_004d:  ldloca.s   V_2
                    IL_004f:  call       "void CollectionExtensions.Report<object>(in System.ReadOnlySpan<object>)"
                    IL_0054:  leave.s    IL_0060
                  }
                  finally
                  {
                    IL_0056:  ldloc.1
                    IL_0057:  brfalse.s  IL_005f
                    IL_0059:  ldloc.1
                    IL_005a:  callvirt   "void System.IDisposable.Dispose()"
                    IL_005f:  endfinally
                  }
                  IL_0060:  ret
                }
                """);
        }

        [Fact]
        public void TopLevelStatement_01()
        {
            string source = """
                using System;
                Span<int?> x = [1, null];
                ReadOnlySpan<object> y = [..x, 3];
                y.Report();
                return y.Length;
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("[1, null, 3], "));
            verifier.VerifyIL("<top-level-statements-entry-point>", """
                {
                  // Code size      143 (0x8f)
                  .maxstack  2
                  .locals init (System.Span<int?> V_0, //x
                                System.ReadOnlySpan<object> V_1, //y
                                <>y__InlineArray2<int?> V_2,
                                System.Collections.Generic.List<object> V_3,
                                System.Span<int?>.Enumerator V_4,
                                int? V_5)
                  IL_0000:  ldloca.s   V_2
                  IL_0002:  initobj    "<>y__InlineArray2<int?>"
                  IL_0008:  ldloca.s   V_2
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray2<int?>, int?>(ref <>y__InlineArray2<int?>, int)"
                  IL_0010:  ldc.i4.1
                  IL_0011:  newobj     "int?..ctor(int)"
                  IL_0016:  stobj      "int?"
                  IL_001b:  ldloca.s   V_2
                  IL_001d:  ldc.i4.1
                  IL_001e:  call       "InlineArrayElementRef<<>y__InlineArray2<int?>, int?>(ref <>y__InlineArray2<int?>, int)"
                  IL_0023:  initobj    "int?"
                  IL_0029:  ldloca.s   V_2
                  IL_002b:  ldc.i4.2
                  IL_002c:  call       "InlineArrayAsSpan<<>y__InlineArray2<int?>, int?>(ref <>y__InlineArray2<int?>, int)"
                  IL_0031:  stloc.0
                  IL_0032:  newobj     "System.Collections.Generic.List<object>..ctor()"
                  IL_0037:  stloc.3
                  IL_0038:  ldloca.s   V_0
                  IL_003a:  call       "System.Span<int?>.Enumerator System.Span<int?>.GetEnumerator()"
                  IL_003f:  stloc.s    V_4
                  IL_0041:  br.s       IL_005e
                  IL_0043:  ldloca.s   V_4
                  IL_0045:  call       "ref int? System.Span<int?>.Enumerator.Current.get"
                  IL_004a:  ldobj      "int?"
                  IL_004f:  stloc.s    V_5
                  IL_0051:  ldloc.3
                  IL_0052:  ldloc.s    V_5
                  IL_0054:  box        "int?"
                  IL_0059:  callvirt   "void System.Collections.Generic.List<object>.Add(object)"
                  IL_005e:  ldloca.s   V_4
                  IL_0060:  call       "bool System.Span<int?>.Enumerator.MoveNext()"
                  IL_0065:  brtrue.s   IL_0043
                  IL_0067:  ldloc.3
                  IL_0068:  ldc.i4.3
                  IL_0069:  box        "int"
                  IL_006e:  callvirt   "void System.Collections.Generic.List<object>.Add(object)"
                  IL_0073:  ldloca.s   V_1
                  IL_0075:  ldloc.3
                  IL_0076:  callvirt   "object[] System.Collections.Generic.List<object>.ToArray()"
                  IL_007b:  call       "System.ReadOnlySpan<object>..ctor(object[])"
                  IL_0080:  ldloca.s   V_1
                  IL_0082:  call       "void CollectionExtensions.Report<object>(in System.ReadOnlySpan<object>)"
                  IL_0087:  ldloca.s   V_1
                  IL_0089:  call       "int System.ReadOnlySpan<object>.Length.get"
                  IL_008e:  ret
                }
                """);
        }

        [Fact]
        public void TopLevelStatement_02()
        {
            string source = """
                using System;

                S.F = [..S.GetSpan(), 3];

                struct S
                {
                    public static Span<int?> GetSpan() => (int?[])[1, null];
                    public static ReadOnlySpan<object> F;
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.VerifyEmitDiagnostics(
                // (3,7): error CS9203: A collection expression of type 'ReadOnlySpan<object>' cannot be used in this context because it may be exposed outside of the current scope.
                // S.F = [..S.GetSpan(), 3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[..S.GetSpan(), 3]").WithArguments("System.ReadOnlySpan<object>").WithLocation(3, 7),
                // (8,19): error CS8345: Field or auto-implemented property cannot be of type 'ReadOnlySpan<object>' unless it is an instance member of a ref struct.
                //     public static ReadOnlySpan<object> F;
                Diagnostic(ErrorCode.ERR_FieldAutoPropCantBeByRefLike, "ReadOnlySpan<object>").WithArguments("System.ReadOnlySpan<object>").WithLocation(8, 19));
        }

        [Fact]
        public void RuntimeHelpers_CreateSpan_Primitives()
        {
            string source = """
                using System;
                class  Program
                {
                    static void Main()
                    {
                        Report<bool>([true]);
                        Report<sbyte>([1]);
                        Report<byte>([2]);
                        Report<short>([3]);
                        Report<ushort>([4]);
                        Report<char>(['5']);
                        Report<int>([6]);
                        Report<uint>([7]);
                        Report<long>([8]);
                        Report<ulong>([9]);
                        Report<float>([10]);
                        Report<double>([11]);
                    }
                    static void Report<T>(ReadOnlySpan<T> s)
                    {
                        s.ToArray().Report(includeType: true);
                        Console.WriteLine();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("""
                    (System.Boolean[]) [True], 
                    (System.SByte[]) [1], 
                    (System.Byte[]) [2], 
                    (System.Int16[]) [3], 
                    (System.UInt16[]) [4], 
                    (System.Char[]) [5], 
                    (System.Int32[]) [6], 
                    (System.UInt32[]) [7], 
                    (System.Int64[]) [8], 
                    (System.UInt64[]) [9], 
                    (System.Single[]) [10], 
                    (System.Double[]) [11], 
                    """));

            verifier.VerifyIL("Program.Main", """
                {
                  // Code size      184 (0xb8)
                  .maxstack  2
                  IL_0000:  ldsflda    "byte <PrivateImplementationDetails>.4BF5122F344554C53BDE2EBB8CD2B7E3D1600AD631C385A5D7CCE23C7785459A"
                  IL_0005:  ldc.i4.1
                  IL_0006:  newobj     "System.ReadOnlySpan<bool>..ctor(void*, int)"
                  IL_000b:  call       "void Program.Report<bool>(System.ReadOnlySpan<bool>)"
                  IL_0010:  ldsflda    "byte <PrivateImplementationDetails>.4BF5122F344554C53BDE2EBB8CD2B7E3D1600AD631C385A5D7CCE23C7785459A"
                  IL_0015:  ldc.i4.1
                  IL_0016:  newobj     "System.ReadOnlySpan<sbyte>..ctor(void*, int)"
                  IL_001b:  call       "void Program.Report<sbyte>(System.ReadOnlySpan<sbyte>)"
                  IL_0020:  ldsflda    "byte <PrivateImplementationDetails>.DBC1B4C900FFE48D575B5DA5C638040125F65DB0FE3E24494B76EA986457D986"
                  IL_0025:  ldc.i4.1
                  IL_0026:  newobj     "System.ReadOnlySpan<byte>..ctor(void*, int)"
                  IL_002b:  call       "void Program.Report<byte>(System.ReadOnlySpan<byte>)"
                  IL_0030:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=2_Align=2 <PrivateImplementationDetails>.9B4FB24EDD6D1D8830E272398263CDBF026B97392CC35387B991DC0248A628F92"
                  IL_0035:  call       "System.ReadOnlySpan<short> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<short>(System.RuntimeFieldHandle)"
                  IL_003a:  call       "void Program.Report<short>(System.ReadOnlySpan<short>)"
                  IL_003f:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=2_Align=2 <PrivateImplementationDetails>.C0BA8A33AC67F44ABFF5984DFBB6F56C46B880AC2B86E1F23E7FA9C402C53AE72"
                  IL_0044:  call       "System.ReadOnlySpan<ushort> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<ushort>(System.RuntimeFieldHandle)"
                  IL_0049:  call       "void Program.Report<ushort>(System.ReadOnlySpan<ushort>)"
                  IL_004e:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=2_Align=2 <PrivateImplementationDetails>.166F829E016F2315A8099E3A8D2DBEC6D91572379FF02C760BA4E0335789D47F2"
                  IL_0053:  call       "System.ReadOnlySpan<char> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<char>(System.RuntimeFieldHandle)"
                  IL_0058:  call       "void Program.Report<char>(System.ReadOnlySpan<char>)"
                  IL_005d:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=4_Align=4 <PrivateImplementationDetails>.7AA8CA4A02506DA9133D8F889678B76F716CE45D02E22FDB7B70A15E56A0EFF84"
                  IL_0062:  call       "System.ReadOnlySpan<int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<int>(System.RuntimeFieldHandle)"
                  IL_0067:  call       "void Program.Report<int>(System.ReadOnlySpan<int>)"
                  IL_006c:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=4_Align=4 <PrivateImplementationDetails>.E8613F5A5BC9F9FEEDA32A8E7C80B69DD4878E47B6A91723FB15EB84236B6A2B4"
                  IL_0071:  call       "System.ReadOnlySpan<uint> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<uint>(System.RuntimeFieldHandle)"
                  IL_0076:  call       "void Program.Report<uint>(System.ReadOnlySpan<uint>)"
                  IL_007b:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=8_Align=8 <PrivateImplementationDetails>.6CC16ABD70EEFB90DC0BA0D14FB088630873B2C6AD943F7442356735984C35A38"
                  IL_0080:  call       "System.ReadOnlySpan<long> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<long>(System.RuntimeFieldHandle)"
                  IL_0085:  call       "void Program.Report<long>(System.ReadOnlySpan<long>)"
                  IL_008a:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=8_Align=8 <PrivateImplementationDetails>.CBBD5F990C53684D7AE650B40FCB5656E02261B53DA5F6A7D8C819C92F2828F88"
                  IL_008f:  call       "System.ReadOnlySpan<ulong> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<ulong>(System.RuntimeFieldHandle)"
                  IL_0094:  call       "void Program.Report<ulong>(System.ReadOnlySpan<ulong>)"
                  IL_0099:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=4_Align=4 <PrivateImplementationDetails>.80C8A717CCD70C8809EB78E6A9591C003E11C721FE0CCAF62FD592ABDA1A55934"
                  IL_009e:  call       "System.ReadOnlySpan<float> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<float>(System.RuntimeFieldHandle)"
                  IL_00a3:  call       "void Program.Report<float>(System.ReadOnlySpan<float>)"
                  IL_00a8:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=8_Align=8 <PrivateImplementationDetails>.9EE2B49423E1506EC86B25B2FEBB317DA93338F594CDCDCD1B38E3A726706DE08"
                  IL_00ad:  call       "System.ReadOnlySpan<double> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<double>(System.RuntimeFieldHandle)"
                  IL_00b2:  call       "void Program.Report<double>(System.ReadOnlySpan<double>)"
                  IL_00b7:  ret
                }
                """);
        }

        [Fact]
        public void RuntimeHelpers_CreateSpan_NotPrimitives()
        {
            string source = """
                using System;
                class  Program
                {
                    static void Main()
                    {
                        Report<object>(["1"]);
                        Report<string>(["2"]);
                        Report<nint>([3]);
                        Report<nuint>([4]);
                    }
                    static void Report<T>(ReadOnlySpan<T> s)
                    {
                        s.ToArray().Report(includeType: true);
                        Console.WriteLine();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Skipped,
                expectedOutput: IncludeExpectedOutput("""
                    (System.Object[]) [1], 
                    (System.String[]) [2], 
                    (System.IntPtr[]) [3], 
                    (System.UIntPtr[]) [4], 
                    """));
            verifier.VerifyIL("Program.Main", """
                {
                    // Code size      135 (0x87)
                    .maxstack  2
                    .locals init (<>y__InlineArray1<object> V_0,
                                <>y__InlineArray1<string> V_1,
                                <>y__InlineArray1<nint> V_2,
                                <>y__InlineArray1<nuint> V_3)
                    IL_0000:  ldloca.s   V_0
                    IL_0002:  initobj    "<>y__InlineArray1<object>"
                    IL_0008:  ldloca.s   V_0
                    IL_000a:  ldc.i4.0
                    IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray1<object>, object>(ref <>y__InlineArray1<object>, int)"
                    IL_0010:  ldstr      "1"
                    IL_0015:  stind.ref
                    IL_0016:  ldloca.s   V_0
                    IL_0018:  ldc.i4.1
                    IL_0019:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<object>, object>(in <>y__InlineArray1<object>, int)"
                    IL_001e:  call       "void Program.Report<object>(System.ReadOnlySpan<object>)"
                    IL_0023:  ldloca.s   V_1
                    IL_0025:  initobj    "<>y__InlineArray1<string>"
                    IL_002b:  ldloca.s   V_1
                    IL_002d:  ldc.i4.0
                    IL_002e:  call       "InlineArrayElementRef<<>y__InlineArray1<string>, string>(ref <>y__InlineArray1<string>, int)"
                    IL_0033:  ldstr      "2"
                    IL_0038:  stind.ref
                    IL_0039:  ldloca.s   V_1
                    IL_003b:  ldc.i4.1
                    IL_003c:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<string>, string>(in <>y__InlineArray1<string>, int)"
                    IL_0041:  call       "void Program.Report<string>(System.ReadOnlySpan<string>)"
                    IL_0046:  ldloca.s   V_2
                    IL_0048:  initobj    "<>y__InlineArray1<nint>"
                    IL_004e:  ldloca.s   V_2
                    IL_0050:  ldc.i4.0
                    IL_0051:  call       "InlineArrayElementRef<<>y__InlineArray1<nint>, nint>(ref <>y__InlineArray1<nint>, int)"
                    IL_0056:  ldc.i4.3
                    IL_0057:  conv.i
                    IL_0058:  stind.i
                    IL_0059:  ldloca.s   V_2
                    IL_005b:  ldc.i4.1
                    IL_005c:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<nint>, nint>(in <>y__InlineArray1<nint>, int)"
                    IL_0061:  call       "void Program.Report<nint>(System.ReadOnlySpan<nint>)"
                    IL_0066:  ldloca.s   V_3
                    IL_0068:  initobj    "<>y__InlineArray1<nuint>"
                    IL_006e:  ldloca.s   V_3
                    IL_0070:  ldc.i4.0
                    IL_0071:  call       "InlineArrayElementRef<<>y__InlineArray1<nuint>, nuint>(ref <>y__InlineArray1<nuint>, int)"
                    IL_0076:  ldc.i4.4
                    IL_0077:  conv.i
                    IL_0078:  stind.i
                    IL_0079:  ldloca.s   V_3
                    IL_007b:  ldc.i4.1
                    IL_007c:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray1<nuint>, nuint>(in <>y__InlineArray1<nuint>, int)"
                    IL_0081:  call       "void Program.Report<nuint>(System.ReadOnlySpan<nuint>)"
                    IL_0086:  ret
                }
                """);
        }

        [Fact]
        public void RuntimeHelpers_CreateSpan_Enums()
        {
            string source = """
                using System;
                enum E_sbyte : sbyte { A = 1 }
                enum E_byte : byte { B = 2 }
                enum E_short : short { C = 3 }
                enum E_ushort : ushort { D = 4 }
                enum E_int : int { E = 5 }
                enum E_uint : uint { F = 6 }
                enum E_long : long { G = 7 }
                enum E_ulong : ulong { H = 8 }
                class  Program
                {
                    static void Main()
                    {
                        Report([E_sbyte.A]);
                        Report([E_byte.B]);
                        Report([E_short.C]);
                        Report([E_ushort.D]);
                        Report([E_int.E]);
                        Report([E_uint.F]);
                        Report([E_long.G]);
                        Report([E_ulong.H]);
                    }
                    static void Report<T>(ReadOnlySpan<T> s)
                    {
                        s.ToArray().Report(includeType: true);
                        Console.WriteLine();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensions },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("""
                    (E_sbyte[]) [A], 
                    (E_byte[]) [B], 
                    (E_short[]) [C], 
                    (E_ushort[]) [D], 
                    (E_int[]) [E], 
                    (E_uint[]) [F], 
                    (E_long[]) [G], 
                    (E_ulong[]) [H], 
                    """));

            verifier.VerifyIL("Program.Main", """
                {
                  // Code size      123 (0x7b)
                  .maxstack  2
                  IL_0000:  ldsflda    "byte <PrivateImplementationDetails>.4BF5122F344554C53BDE2EBB8CD2B7E3D1600AD631C385A5D7CCE23C7785459A"
                  IL_0005:  ldc.i4.1
                  IL_0006:  newobj     "System.ReadOnlySpan<E_sbyte>..ctor(void*, int)"
                  IL_000b:  call       "void Program.Report<E_sbyte>(System.ReadOnlySpan<E_sbyte>)"
                  IL_0010:  ldsflda    "byte <PrivateImplementationDetails>.DBC1B4C900FFE48D575B5DA5C638040125F65DB0FE3E24494B76EA986457D986"
                  IL_0015:  ldc.i4.1
                  IL_0016:  newobj     "System.ReadOnlySpan<E_byte>..ctor(void*, int)"
                  IL_001b:  call       "void Program.Report<E_byte>(System.ReadOnlySpan<E_byte>)"
                  IL_0020:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=2_Align=2 <PrivateImplementationDetails>.9B4FB24EDD6D1D8830E272398263CDBF026B97392CC35387B991DC0248A628F92"
                  IL_0025:  call       "System.ReadOnlySpan<E_short> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<E_short>(System.RuntimeFieldHandle)"
                  IL_002a:  call       "void Program.Report<E_short>(System.ReadOnlySpan<E_short>)"
                  IL_002f:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=2_Align=2 <PrivateImplementationDetails>.C0BA8A33AC67F44ABFF5984DFBB6F56C46B880AC2B86E1F23E7FA9C402C53AE72"
                  IL_0034:  call       "System.ReadOnlySpan<E_ushort> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<E_ushort>(System.RuntimeFieldHandle)"
                  IL_0039:  call       "void Program.Report<E_ushort>(System.ReadOnlySpan<E_ushort>)"
                  IL_003e:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=4_Align=4 <PrivateImplementationDetails>.2594B6A92EBFB1C3312DEB7D01C015FB95E9FBE9BD7BC6B527AF07813EC7B9104"
                  IL_0043:  call       "System.ReadOnlySpan<E_int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<E_int>(System.RuntimeFieldHandle)"
                  IL_0048:  call       "void Program.Report<E_int>(System.ReadOnlySpan<E_int>)"
                  IL_004d:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=4_Align=4 <PrivateImplementationDetails>.7AA8CA4A02506DA9133D8F889678B76F716CE45D02E22FDB7B70A15E56A0EFF84"
                  IL_0052:  call       "System.ReadOnlySpan<E_uint> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<E_uint>(System.RuntimeFieldHandle)"
                  IL_0057:  call       "void Program.Report<E_uint>(System.ReadOnlySpan<E_uint>)"
                  IL_005c:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=8_Align=8 <PrivateImplementationDetails>.AAE89FC0F03E2959AE4D701A80CC3915918C950B159F6ABB6C92C1433B1A85348"
                  IL_0061:  call       "System.ReadOnlySpan<E_long> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<E_long>(System.RuntimeFieldHandle)"
                  IL_0066:  call       "void Program.Report<E_long>(System.ReadOnlySpan<E_long>)"
                  IL_006b:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=8_Align=8 <PrivateImplementationDetails>.6CC16ABD70EEFB90DC0BA0D14FB088630873B2C6AD943F7442356735984C35A38"
                  IL_0070:  call       "System.ReadOnlySpan<E_ulong> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<E_ulong>(System.RuntimeFieldHandle)"
                  IL_0075:  call       "void Program.Report<E_ulong>(System.ReadOnlySpan<E_ulong>)"
                  IL_007a:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void RuntimeHelpers_CreateSpan([CombinatorialValues(TargetFramework.Net60, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class  Program
                {
                    static void Main()
                    {
                        F1().Report();
                        F2().Report();
                    }
                    static ReadOnlySpan<int> F1() => new[] { 1, 2, 3 };
                    static ReadOnlySpan<int> F2() => [1, 2, 3];
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: targetFramework,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], [1, 2, 3], "));

            string expectedIL = targetFramework == TargetFramework.Net60 ?
                """
                {
                  // Code size       38 (0x26)
                  .maxstack  3
                  IL_0000:  ldsfld     "int[] <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D_A6"
                  IL_0005:  dup
                  IL_0006:  brtrue.s   IL_0020
                  IL_0008:  pop
                  IL_0009:  ldc.i4.3
                  IL_000a:  newarr     "int"
                  IL_000f:  dup
                  IL_0010:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12 <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D"
                  IL_0015:  call       "void System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(System.Array, System.RuntimeFieldHandle)"
                  IL_001a:  dup
                  IL_001b:  stsfld     "int[] <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D_A6"
                  IL_0020:  newobj     "System.ReadOnlySpan<int>..ctor(int[])"
                  IL_0025:  ret
                }
                """ :
                """
                {
                  // Code size       11 (0xb)
                  .maxstack  1
                  IL_0000:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12_Align=4 <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D4"
                  IL_0005:  call       "System.ReadOnlySpan<int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<int>(System.RuntimeFieldHandle)"
                  IL_000a:  ret
                }
                """;
            verifier.VerifyIL("Program.F1", expectedIL);
            verifier.VerifyIL("Program.F2", expectedIL);
        }

        [CombinatorialData]
        [Theory]
        public void RuntimeHelpers_CreateSpan_Byte([CombinatorialValues(TargetFramework.Net60, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class  Program
                {
                    static void Main()
                    {
                        F1().Report();
                        F2().Report();
                    }
                    static ReadOnlySpan<byte> F1()
                    {
                        ReadOnlySpan<byte> s = new byte[] { 1, 2, 3 };
                        return s;
                    }
                    static ReadOnlySpan<byte> F2()
                    {
                        ReadOnlySpan<byte> s = [1, 2, 3];
                        return s;
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: targetFramework,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], [1, 2, 3], "));

            string expectedIL =
                """
                {
                  // Code size       12 (0xc)
                  .maxstack  2
                  IL_0000:  ldsflda    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=3 <PrivateImplementationDetails>.039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81"
                  IL_0005:  ldc.i4.3
                  IL_0006:  newobj     "System.ReadOnlySpan<byte>..ctor(void*, int)"
                  IL_000b:  ret
                }
                """;
            verifier.VerifyIL("Program.F1", expectedIL);
            verifier.VerifyIL("Program.F2", expectedIL);
        }

        [CombinatorialData]
        [Theory]
        public void RuntimeHelpers_CreateSpan_NotApplicable_01([CombinatorialValues(TargetFramework.Net60, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string source = """
                using System;
                class  Program
                {
                    static Span<int> NotReadOnlySpan() => [1, 2, 3];
                    static ReadOnlySpan<int> NotConstants(int c) => [1, 2, c];
                }
                """;
            var comp = CreateCompilation(source, targetFramework: targetFramework);
            comp.VerifyEmitDiagnostics(
                // (4,43): error CS9203: A collection expression of type 'Span<int>' cannot be used in this context because it may be exposed outside of the current scope.
                //     static Span<int> NotReadOnlySpan() => [1, 2, 3];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[1, 2, 3]").WithArguments("System.Span<int>").WithLocation(4, 43),
                // (5,53): error CS9203: A collection expression of type 'ReadOnlySpan<int>' cannot be used in this context because it may be exposed outside of the current scope.
                //     static ReadOnlySpan<int> NotConstants(int c) => [1, 2, c];
                Diagnostic(ErrorCode.ERR_CollectionExpressionEscape, "[1, 2, c]").WithArguments("System.ReadOnlySpan<int>").WithLocation(5, 53));
        }

        [Fact]
        public void RuntimeHelpers_CreateSpan_NotApplicable_02()
        {
            string source = """
                using System;
                class  Program
                {
                    static void Main()
                    {
                        NotReadOnlySpan();
                        NotConstants(3);
                    }
                    static void NotReadOnlySpan()
                    {
                        Span<int> s = [1, 2, 3];
                        s.Report();
                    }
                    static void NotConstants(int c)
                    {
                        ReadOnlySpan<int> s =[1, 2, c];
                        s.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { source, s_collectionExtensionsWithSpan },
                targetFramework: TargetFramework.Net80,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], [1, 2, 3], "));
            verifier.VerifyIL("Program.NotReadOnlySpan", """
                {
                  // Code size       55 (0x37)
                  .maxstack  2
                  .locals init (System.Span<int> V_0, //s
                                <>y__InlineArray3<int> V_1)
                  IL_0000:  ldloca.s   V_1
                  IL_0002:  initobj    "<>y__InlineArray3<int>"
                  IL_0008:  ldloca.s   V_1
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_0010:  ldc.i4.1
                  IL_0011:  stind.i4
                  IL_0012:  ldloca.s   V_1
                  IL_0014:  ldc.i4.1
                  IL_0015:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_001a:  ldc.i4.2
                  IL_001b:  stind.i4
                  IL_001c:  ldloca.s   V_1
                  IL_001e:  ldc.i4.2
                  IL_001f:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_0024:  ldc.i4.3
                  IL_0025:  stind.i4
                  IL_0026:  ldloca.s   V_1
                  IL_0028:  ldc.i4.3
                  IL_0029:  call       "InlineArrayAsSpan<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_002e:  stloc.0
                  IL_002f:  ldloca.s   V_0
                  IL_0031:  call       "void CollectionExtensions.Report<int>(in System.Span<int>)"
                  IL_0036:  ret
                }
                """);
            verifier.VerifyIL("Program.NotConstants", """
                {
                  // Code size       55 (0x37)
                  .maxstack  2
                  .locals init (System.ReadOnlySpan<int> V_0, //s
                                <>y__InlineArray3<int> V_1)
                  IL_0000:  ldloca.s   V_1
                  IL_0002:  initobj    "<>y__InlineArray3<int>"
                  IL_0008:  ldloca.s   V_1
                  IL_000a:  ldc.i4.0
                  IL_000b:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_0010:  ldc.i4.1
                  IL_0011:  stind.i4
                  IL_0012:  ldloca.s   V_1
                  IL_0014:  ldc.i4.1
                  IL_0015:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_001a:  ldc.i4.2
                  IL_001b:  stind.i4
                  IL_001c:  ldloca.s   V_1
                  IL_001e:  ldc.i4.2
                  IL_001f:  call       "InlineArrayElementRef<<>y__InlineArray3<int>, int>(ref <>y__InlineArray3<int>, int)"
                  IL_0024:  ldarg.0
                  IL_0025:  stind.i4
                  IL_0026:  ldloca.s   V_1
                  IL_0028:  ldc.i4.3
                  IL_0029:  call       "InlineArrayAsReadOnlySpan<<>y__InlineArray3<int>, int>(in <>y__InlineArray3<int>, int)"
                  IL_002e:  stloc.0
                  IL_002f:  ldloca.s   V_0
                  IL_0031:  call       "void CollectionExtensions.Report<int>(in System.ReadOnlySpan<int>)"
                  IL_0036:  ret
                }
                """);
        }

        [CombinatorialData]
        [Theory]
        public void RuntimeHelpers_CreateSpan_RefStruct([CombinatorialValues(TargetFramework.Net60, TargetFramework.Net80)] TargetFramework targetFramework)
        {
            string sourceA = $$"""
                using System;
                using System.Collections.Generic;
                using System.Runtime.CompilerServices;
                [CollectionBuilder(typeof(MyCollectionBuilder), nameof(MyCollectionBuilder.Create))]
                public ref struct MyCollection<T>
                {
                    private readonly List<T> _list;
                    public MyCollection(List<T> list) { _list = list; }
                    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
                }
                public class MyCollectionBuilder
                {
                    public static MyCollection<T> Create<T>(ReadOnlySpan<T> items)
                        => new MyCollection<T>(new List<T>(items.ToArray()));
                }
                """;
            var comp = CreateCompilation(
                targetFramework == TargetFramework.Net80 ? new[] { sourceA } : new[] { sourceA, CollectionBuilderAttributeDefinition },
                targetFramework: targetFramework);
            comp.VerifyEmitDiagnostics();
            var refA = comp.EmitToImageReference();

            string sourceB = $$"""
                using System.Collections.Generic;
                using System;
                enum E : byte { A = 1, B = 2, C = 3 }
                class  Program
                {
                    static void Main()
                    {
                        MyCollection<byte> x = F1();
                        MyCollection<int> y = F2();
                        MyCollection<E> z = F3();
                        Report(x);
                        Report(y);
                        Report(z);
                    }
                    static MyCollection<byte> F1() => [1, 2, 3];
                    static MyCollection<int> F2() => [1, 2, 3];
                    static MyCollection<E> F3() => [E.A, E.B, E.C];
                    static void Report<T>(MyCollection<T> c)
                    {
                        var list = new List<T>();
                        foreach (var i in c) list.Add(i);
                        list.Report();
                    }
                }
                """;
            var verifier = CompileAndVerify(
                new[] { sourceB, s_collectionExtensions },
                references: new[] { refA },
                targetFramework: targetFramework,
                verify: Verification.Fails,
                expectedOutput: IncludeExpectedOutput("[1, 2, 3], [1, 2, 3], [A, B, C], "));

            verifier.VerifyIL("Program.F1", """
                {
                  // Code size       17 (0x11)
                  .maxstack  2
                  IL_0000:  ldsflda    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=3 <PrivateImplementationDetails>.039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81"
                  IL_0005:  ldc.i4.3
                  IL_0006:  newobj     "System.ReadOnlySpan<byte>..ctor(void*, int)"
                  IL_000b:  call       "MyCollection<byte> MyCollectionBuilder.Create<byte>(System.ReadOnlySpan<byte>)"
                  IL_0010:  ret
                }
                """);
            if (targetFramework == TargetFramework.Net60)
            {
                verifier.VerifyIL("Program.F2", """
                    {
                      // Code size       43 (0x2b)
                      .maxstack  3
                      IL_0000:  ldsfld     "int[] <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D_A6"
                      IL_0005:  dup
                      IL_0006:  brtrue.s   IL_0020
                      IL_0008:  pop
                      IL_0009:  ldc.i4.3
                      IL_000a:  newarr     "int"
                      IL_000f:  dup
                      IL_0010:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12 <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D"
                      IL_0015:  call       "void System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(System.Array, System.RuntimeFieldHandle)"
                      IL_001a:  dup
                      IL_001b:  stsfld     "int[] <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D_A6"
                      IL_0020:  newobj     "System.ReadOnlySpan<int>..ctor(int[])"
                      IL_0025:  call       "MyCollection<int> MyCollectionBuilder.Create<int>(System.ReadOnlySpan<int>)"
                      IL_002a:  ret
                    }
                    """);
            }
            else
            {
                verifier.VerifyIL("Program.F2", """
                    {
                      // Code size       16 (0x10)
                      .maxstack  1
                      IL_0000:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12_Align=4 <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D4"
                      IL_0005:  call       "System.ReadOnlySpan<int> System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan<int>(System.RuntimeFieldHandle)"
                      IL_000a:  call       "MyCollection<int> MyCollectionBuilder.Create<int>(System.ReadOnlySpan<int>)"
                      IL_000f:  ret
                    }
                    """);
            }
            verifier.VerifyIL("Program.F3", """
                {
                    // Code size       17 (0x11)
                    .maxstack  2
                    IL_0000:  ldsflda    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=3 <PrivateImplementationDetails>.039058C6F2C0CB492C533B0A4D14EF77CC0F78ABCCCED5287D84A1A2011CFB81"
                    IL_0005:  ldc.i4.3
                    IL_0006:  newobj     "System.ReadOnlySpan<E>..ctor(void*, int)"
                    IL_000b:  call       "MyCollection<E> MyCollectionBuilder.Create<E>(System.ReadOnlySpan<E>)"
                    IL_0010:  ret
                }
                """);
        }

        [Fact]
        public void RuntimeHelpers_CreateSpan_MissingCreateSpan()
        {
            string source = """
                using System;
                class  Program
                {
                    static void Main()
                    {
                        ReadOnlySpan<int> s = [1, 2, 3];
                        s.Report();
                    }
                }
                """;
            var comp = CreateCompilation(new[] { source, s_collectionExtensionsWithSpan }, targetFramework: TargetFramework.Net80, options: TestOptions.ReleaseExe);
            comp.MakeMemberMissing(WellKnownMember.System_Runtime_CompilerServices_RuntimeHelpers__CreateSpanRuntimeFieldHandle);

            var verifier = CompileAndVerify(comp, verify: Verification.Fails, expectedOutput: IncludeExpectedOutput("[1, 2, 3], "));
            verifier.VerifyIL("Program.Main", """
                {
                  // Code size       46 (0x2e)
                  .maxstack  3
                  .locals init (System.ReadOnlySpan<int> V_0) //s
                  IL_0000:  ldsfld     "int[] <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D_A6"
                  IL_0005:  dup
                  IL_0006:  brtrue.s   IL_0020
                  IL_0008:  pop
                  IL_0009:  ldc.i4.3
                  IL_000a:  newarr     "int"
                  IL_000f:  dup
                  IL_0010:  ldtoken    "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=12 <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D"
                  IL_0015:  call       "void System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(System.Array, System.RuntimeFieldHandle)"
                  IL_001a:  dup
                  IL_001b:  stsfld     "int[] <PrivateImplementationDetails>.4636993D3E1DA4E9D6B8F87B79E8F7C6D018580D52661950EABC3845C5897A4D_A6"
                  IL_0020:  newobj     "System.ReadOnlySpan<int>..ctor(int[])"
                  IL_0025:  stloc.0
                  IL_0026:  ldloca.s   V_0
                  IL_0028:  call       "void CollectionExtensions.Report<int>(in System.ReadOnlySpan<int>)"
                  IL_002d:  ret
                }
                """);
        }

        [Fact]
        public void RuntimeHelpers_CreateSpan_MissingImplicitOperator()
        {
            string source = """
                using System;
                class  Program
                {
                    static void Main()
                    {
                        ReadOnlySpan<int> s = [1, 2, 3];
                    }
                }
                """;
            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net80);
            comp.MakeMemberMissing(WellKnownMember.System_ReadOnlySpan_T__op_Implicit_ReadOnlySpan_T_Array);
            comp.VerifyEmitDiagnostics(
                // (6,31): error CS0656: Missing compiler required member 'System.ReadOnlySpan`1.op_Implicit'
                //         ReadOnlySpan<int> s = [1, 2, 3];
                Diagnostic(ErrorCode.ERR_MissingPredefinedMember, "[1, 2, 3]").WithArguments("System.ReadOnlySpan`1", "op_Implicit").WithLocation(6, 31));
        }

        [Fact]
        public void ExpressionTrees()
        {
            string source = """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Linq.Expressions;
                interface I<T> : IEnumerable
                {
                    void Add(T t);
                }
                class Program
                {
                    static Expression<Func<int[]>> Create1()
                    {
                        return () => [];
                    }
                    static Expression<Func<List<object>>> Create2()
                    {
                        return () => [1, 2];
                    }
                    static Expression<Func<T>> Create3<T, U>(U a, U b) where T : I<U>, new()
                    {
                        return () => [a, b];
                    }
                }
                """;
            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics(
                // (13,22): error CS9175: An expression tree may not contain a collection expression.
                //         return () => [];
                Diagnostic(ErrorCode.ERR_ExpressionTreeContainsCollectionExpression, "[]").WithLocation(13, 22),
                // (17,22): error CS9175: An expression tree may not contain a collection expression.
                //         return () => [1, 2];
                Diagnostic(ErrorCode.ERR_ExpressionTreeContainsCollectionExpression, "[1, 2]").WithLocation(17, 22),
                // (21,22): error CS9175: An expression tree may not contain a collection expression.
                //         return () => [a, b];
                Diagnostic(ErrorCode.ERR_ExpressionTreeContainsCollectionExpression, "[a, b]").WithLocation(21, 22));
        }

        [Fact]
        public void IOperation_Array()
        {
            string source = """
                class Program
                {
                    static T[] Create<T>(T a, T b)
                    {
                        return /*<bind>*/[a, b]/*</bind>*/;
                    }
                }
                """;

            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();

            VerifyOperationTreeForTest<CollectionExpressionSyntax>(comp,
                """
                IOperation:  (OperationKind.None, Type: T[]) (Syntax: '[a, b]')
                  Children(2):
                      IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'a')
                      IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'b')
                """);

            var tree = comp.SyntaxTrees[0];
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Create");
            VerifyFlowGraph(comp, method,
                """
                Block[B0] - Entry
                    Statements (0)
                    Next (Regular) Block[B1]
                Block[B1] - Block
                    Predecessors: [B0]
                    Statements (0)
                    Next (Return) Block[B2]
                        IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: T[], IsImplicit) (Syntax: '[a, b]')
                          Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                            (CollectionExpression)
                          Operand:
                            IOperation:  (OperationKind.None, Type: T[]) (Syntax: '[a, b]')
                              Children(2):
                                  IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'a')
                                  IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'b')
                Block[B2] - Exit
                    Predecessors: [B1]
                    Statements (0)
                """);
        }

        [Fact]
        public void IOperation_Span()
        {
            string source = """
                using System;
                class Program
                {
                    static void Create<T>(T a, T b)
                    {
                        Span<T> s = /*<bind>*/[a, b]/*</bind>*/;
                    }
                }
                """;

            var comp = CreateCompilation(source, targetFramework: TargetFramework.Net70);
            comp.VerifyEmitDiagnostics();

            VerifyOperationTreeForTest<CollectionExpressionSyntax>(comp,
                """
                IOperation:  (OperationKind.None, Type: System.Span<T>) (Syntax: '[a, b]')
                  Children(2):
                      IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'a')
                      IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'b')
                """);

            var tree = comp.SyntaxTrees[0];
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Create");
            VerifyFlowGraph(comp, method,
                """
                Block[B0] - Entry
                    Statements (0)
                    Next (Regular) Block[B1]
                        Entering: {R1}
                .locals {R1}
                {
                    Locals: [System.Span<T> s]
                    Block[B1] - Block
                        Predecessors: [B0]
                        Statements (1)
                            ISimpleAssignmentOperation (OperationKind.SimpleAssignment, Type: System.Span<T>, IsImplicit) (Syntax: 's = /*<bind>*/[a, b]')
                              Left:
                                ILocalReferenceOperation: s (IsDeclaration: True) (OperationKind.LocalReference, Type: System.Span<T>, IsImplicit) (Syntax: 's = /*<bind>*/[a, b]')
                              Right:
                                IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Span<T>, IsImplicit) (Syntax: '[a, b]')
                                  Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                                    (CollectionExpression)
                                  Operand:
                                    IOperation:  (OperationKind.None, Type: System.Span<T>) (Syntax: '[a, b]')
                                      Children(2):
                                          IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'a')
                                          IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: T, IsImplicit) (Syntax: 'b')
                        Next (Regular) Block[B2]
                            Leaving: {R1}
                }
                Block[B2] - Exit
                    Predecessors: [B1]
                    Statements (0)
                """);
        }

        [Fact]
        public void IOperation_CollectionInitializer()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                interface I<T> : IEnumerable<T>
                {
                    void Add(T t);
                }
                struct S<T> : I<T>
                {
                    public void Add(T t) { }
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw null;
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                class Program
                {
                    static S<T> Create<T>(T a, T b)
                    {
                        return /*<bind>*/[a, b]/*</bind>*/;
                    }
                }
                """;

            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();

            VerifyOperationTreeForTest<CollectionExpressionSyntax>(comp,
                """
                IOperation:  (OperationKind.None, Type: S<T>) (Syntax: '[a, b]')
                  Children(2):
                      IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: T) (Syntax: 'a')
                      IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: T) (Syntax: 'b')
                """);

            var tree = comp.SyntaxTrees[0];
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Create");
            VerifyFlowGraph(comp, method,
                """
                Block[B0] - Entry
                    Statements (0)
                    Next (Regular) Block[B1]
                Block[B1] - Block
                    Predecessors: [B0]
                    Statements (0)
                    Next (Return) Block[B2]
                        IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: S<T>, IsImplicit) (Syntax: '[a, b]')
                          Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                            (CollectionExpression)
                          Operand:
                            IOperation:  (OperationKind.None, Type: S<T>) (Syntax: '[a, b]')
                              Children(2):
                                  IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: T) (Syntax: 'a')
                                  IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: T) (Syntax: 'b')
                Block[B2] - Exit
                    Predecessors: [B1]
                    Statements (0)
                """);
        }

        [Fact]
        public void IOperation_TypeParameter()
        {
            string source = """
                using System.Collections;
                using System.Collections.Generic;
                interface I<T> : IEnumerable<T>
                {
                    void Add(T t);
                }
                struct S<T> : I<T>
                {
                    public void Add(T t) { }
                    IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw null;
                    IEnumerator IEnumerable.GetEnumerator() => throw null;
                }
                class Program
                {
                    static T Create<T, U>(U a, U b) where T : I<U>, new()
                    {
                        return /*<bind>*/[a, b]/*</bind>*/;
                    }
                }
                """;

            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();

            VerifyOperationTreeForTest<CollectionExpressionSyntax>(comp,
                """
                IOperation:  (OperationKind.None, Type: T) (Syntax: '[a, b]')
                  Children(2):
                      IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: U) (Syntax: 'a')
                      IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: U) (Syntax: 'b')
                """);

            var tree = comp.SyntaxTrees[0];
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Create");
            VerifyFlowGraph(comp, method,
                """
                Block[B0] - Entry
                    Statements (0)
                    Next (Regular) Block[B1]
                Block[B1] - Block
                    Predecessors: [B0]
                    Statements (0)
                    Next (Return) Block[B2]
                        IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: T, IsImplicit) (Syntax: '[a, b]')
                          Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                            (CollectionExpression)
                          Operand:
                            IOperation:  (OperationKind.None, Type: T) (Syntax: '[a, b]')
                              Children(2):
                                  IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: U) (Syntax: 'a')
                                  IParameterReferenceOperation: b (OperationKind.ParameterReference, Type: U) (Syntax: 'b')
                Block[B2] - Exit
                    Predecessors: [B1]
                    Statements (0)
                """);
        }

        [Fact]
        public void IOperation_Nested()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static void Main()
                    {
                        List<List<int>> x = /*<bind>*/[[Get(1)]]/*</bind>*/;
                    }
                    static int Get(int value) => value;
                }
                """;

            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();

            VerifyOperationTreeForTest<CollectionExpressionSyntax>(comp,
                """
                IOperation:  (OperationKind.None, Type: System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>) (Syntax: '[[Get(1)]]')
                  Children(1):
                      IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.Generic.List<System.Int32>, IsImplicit) (Syntax: '[Get(1)]')
                        Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                        Operand:
                          IOperation:  (OperationKind.None, Type: System.Collections.Generic.List<System.Int32>) (Syntax: '[Get(1)]')
                            Children(1):
                                IInvocationOperation (System.Int32 Program.Get(System.Int32 value)) (OperationKind.Invocation, Type: System.Int32) (Syntax: 'Get(1)')
                                  Instance Receiver:
                                    null
                                  Arguments(1):
                                      IArgumentOperation (ArgumentKind.Explicit, Matching Parameter: value) (OperationKind.Argument, Type: null) (Syntax: '1')
                                        ILiteralOperation (OperationKind.Literal, Type: System.Int32, Constant: 1) (Syntax: '1')
                                        InConversion: CommonConversion (Exists: True, IsIdentity: True, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                                        OutConversion: CommonConversion (Exists: True, IsIdentity: True, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                """);

            var tree = comp.SyntaxTrees[0];
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Main");
            VerifyFlowGraph(comp, method,
                """
                Block[B0] - Entry
                    Statements (0)
                    Next (Regular) Block[B1]
                        Entering: {R1}
                .locals {R1}
                {
                    Locals: [System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>> x]
                    Block[B1] - Block
                        Predecessors: [B0]
                        Statements (1)
                            ISimpleAssignmentOperation (OperationKind.SimpleAssignment, Type: System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>, IsImplicit) (Syntax: 'x = /*<bind>*/[[Get(1)]]')
                              Left:
                                ILocalReferenceOperation: x (IsDeclaration: True) (OperationKind.LocalReference, Type: System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>, IsImplicit) (Syntax: 'x = /*<bind>*/[[Get(1)]]')
                              Right:
                                IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>, IsImplicit) (Syntax: '[[Get(1)]]')
                                  Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                                    (CollectionExpression)
                                  Operand:
                                    IOperation:  (OperationKind.None, Type: System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>) (Syntax: '[[Get(1)]]')
                                      Children(1):
                                          IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.Generic.List<System.Int32>, IsImplicit) (Syntax: '[Get(1)]')
                                            Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                                              (CollectionExpression)
                                            Operand:
                                              IOperation:  (OperationKind.None, Type: System.Collections.Generic.List<System.Int32>) (Syntax: '[Get(1)]')
                                                Children(1):
                                                    IInvocationOperation (System.Int32 Program.Get(System.Int32 value)) (OperationKind.Invocation, Type: System.Int32) (Syntax: 'Get(1)')
                                                      Instance Receiver:
                                                        null
                                                      Arguments(1):
                                                          IArgumentOperation (ArgumentKind.Explicit, Matching Parameter: value) (OperationKind.Argument, Type: null) (Syntax: '1')
                                                            ILiteralOperation (OperationKind.Literal, Type: System.Int32, Constant: 1) (Syntax: '1')
                                                            InConversion: CommonConversion (Exists: True, IsIdentity: True, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                                                            OutConversion: CommonConversion (Exists: True, IsIdentity: True, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                        Next (Regular) Block[B2]
                            Leaving: {R1}
                }
                Block[B2] - Exit
                    Predecessors: [B1]
                    Statements (0)
                """);
        }

        [Fact]
        public void IOperation_SpreadElement_01()
        {
            string source = """
                class Program
                {
                    static int[] Append(int[] a)
                    {
                        return /*<bind>*/[..a]/*</bind>*/;
                    }
                }
                """;

            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();

            VerifyOperationTreeForTest<CollectionExpressionSyntax>(comp,
                """
                IOperation:  (OperationKind.None, Type: System.Int32[]) (Syntax: '[..a]')
                  Children(1):
                      IOperation:  (OperationKind.None, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: '..a')
                        Children(1):
                            IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: 'a')
                              Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: True, IsUserDefined: False) (MethodSymbol: null)
                              Operand:
                                IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: System.Int32[]) (Syntax: 'a')
                """);

            var tree = comp.SyntaxTrees[0];
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Append");
            VerifyFlowGraph(comp, method,
                """
                Block[B0] - Entry
                    Statements (0)
                    Next (Regular) Block[B1]
                Block[B1] - Block
                    Predecessors: [B0]
                    Statements (0)
                    Next (Return) Block[B2]
                        IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Int32[], IsImplicit) (Syntax: '[..a]')
                          Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                            (CollectionExpression)
                          Operand:
                            IOperation:  (OperationKind.None, Type: System.Int32[]) (Syntax: '[..a]')
                              Children(1):
                                  IOperation:  (OperationKind.None, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: '..a')
                                    Children(1):
                                        IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: 'a')
                                          Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: True, IsUserDefined: False) (MethodSymbol: null)
                                            (ImplicitReference)
                                          Operand:
                                            IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: System.Int32[]) (Syntax: 'a')
                Block[B2] - Exit
                    Predecessors: [B1]
                    Statements (0)
                """);
        }

        [Fact]
        public void IOperation_SpreadElement_02()
        {
            string source = """
                using System.Collections.Generic;
                class Program
                {
                    static List<int> Append(int[] a)
                    {
                        return /*<bind>*/[..a]/*</bind>*/;
                    }
                }
                """;

            var comp = CreateCompilation(source);
            comp.VerifyEmitDiagnostics();

            VerifyOperationTreeForTest<CollectionExpressionSyntax>(comp,
                """
                IOperation:  (OperationKind.None, Type: System.Collections.Generic.List<System.Int32>) (Syntax: '[..a]')
                  Children(1):
                      IOperation:  (OperationKind.None, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: '..a')
                        Children(1):
                            IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: 'a')
                              Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: True, IsUserDefined: False) (MethodSymbol: null)
                              Operand:
                                IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: System.Int32[]) (Syntax: 'a')
                """);

            var tree = comp.SyntaxTrees[0];
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Append");
            VerifyFlowGraph(comp, method,
                """
                Block[B0] - Entry
                    Statements (0)
                    Next (Regular) Block[B1]
                Block[B1] - Block
                    Predecessors: [B0]
                    Statements (0)
                    Next (Return) Block[B2]
                        IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.Generic.List<System.Int32>, IsImplicit) (Syntax: '[..a]')
                          Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: False, IsUserDefined: False) (MethodSymbol: null)
                            (CollectionExpression)
                          Operand:
                            IOperation:  (OperationKind.None, Type: System.Collections.Generic.List<System.Int32>) (Syntax: '[..a]')
                              Children(1):
                                  IOperation:  (OperationKind.None, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: '..a')
                                    Children(1):
                                        IConversionOperation (TryCast: False, Unchecked) (OperationKind.Conversion, Type: System.Collections.IEnumerable, IsImplicit) (Syntax: 'a')
                                          Conversion: CommonConversion (Exists: True, IsIdentity: False, IsNumeric: False, IsReference: True, IsUserDefined: False) (MethodSymbol: null)
                                            (ImplicitReference)
                                          Operand:
                                            IParameterReferenceOperation: a (OperationKind.ParameterReference, Type: System.Int32[]) (Syntax: 'a')
                Block[B2] - Exit
                    Predecessors: [B1]
                    Statements (0)
                """);
        }

        [Fact]
        public void Async_01()
        {
            string source = """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class Program
                {
                    static async Task Main()
                    {
                        (await CreateArray()).Report();
                        (await CreateList()).Report();
                    }
                    static async Task<int[]> CreateArray()
                    {
                        return [await F(1), await F(2)];
                    }
                    static async Task<List<int>> CreateList()
                    {
                        return [await F(3), await F(4)];
                    }
                    static async Task<int> F(int i)
                    {
                        await Task.Yield();
                        return i;
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[1, 2], [3, 4], ");
        }

        [Fact]
        public void Async_02()
        {
            string source = """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                class Program
                {
                    static async Task Main()
                    {
                        (await F2(F1())).Report();
                    }
                    static async Task<int[]> F1()
                    {
                        return [await F(1), await F(2)];
                    }
                    static async Task<int[]> F2(Task<int[]> e)
                    {
                        return [3, .. await e, 4];
                    }
                    static async Task<T> F<T>(T t)
                    {
                        await Task.Yield();
                        return t;
                    }
                }
                """;
            CompileAndVerify(new[] { source, s_collectionExtensions }, expectedOutput: "[3, 1, 2, 4], ");
        }

        [Fact]
        public void PostfixIncrementDecrement()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main()
                    {
                        []++;
                        []--;
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,9): error CS1059: The operand of an increment or decrement operator must be a variable, property or indexer
                //         []++;
                Diagnostic(ErrorCode.ERR_IncrementLvalueExpected, "[]").WithLocation(7, 9),
                // (8,9): error CS1059: The operand of an increment or decrement operator must be a variable, property or indexer
                //         []--;
                Diagnostic(ErrorCode.ERR_IncrementLvalueExpected, "[]").WithLocation(8, 9));
        }

        [Fact]
        public void PostfixPointerAccess()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main()
                    {
                        var v = []->Count;
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,17): error CS9503: There is no target type for the collection expression.
                //         var v = []->Count;
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 17));
        }

        [Fact]
        public void LeftHandAssignment()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main()
                    {
                        [] = null;
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,9): error CS0131: The left-hand side of an assignment must be a variable, property or indexer
                //         [] = null;
                Diagnostic(ErrorCode.ERR_AssgLvalueExpected, "[]").WithLocation(7, 9));
        }

        [Fact]
        public void BinaryOperator()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main(List<int> list)
                    {
                        [] + list;
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,9): error CS1729: 'string' does not contain a constructor that takes 0 arguments
                //         [] + list;
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "[]").WithArguments("string", "0").WithLocation(7, 9),
                // (7,9): error CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement
                //         [] + list;
                Diagnostic(ErrorCode.ERR_IllegalStatement, "[] + list").WithLocation(7, 9));
        }

        [Fact]
        public void RangeOperator()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main(List<int> list)
                    {
                        []..;
                    }
                }
                """;
            CreateCompilationWithIndexAndRangeAndSpan(source).VerifyEmitDiagnostics(
                // (7,9): error CS9500: Cannot initialize type 'Index' with a collection expression because the type is not constructible.
                //         []..;
                Diagnostic(ErrorCode.ERR_CollectionExpressionTargetTypeNotConstructible, "[]").WithArguments("System.Index").WithLocation(7, 9),
                // (7,9): error CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement
                //         []..;
                Diagnostic(ErrorCode.ERR_IllegalStatement, "[]..").WithLocation(7, 9));
        }

        [Fact]
        public void TopLevelSwitchExpression()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main(List<int> list)
                    {
                        [] switch { null => 0 };
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,9): error CS9503: There is no target type for the collection expression.
                //         [] switch
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 9),
                // (7,9): error CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement
                //         [] switch
                Diagnostic(ErrorCode.ERR_IllegalStatement, "[] switch { null => 0 }").WithLocation(7, 9));
        }

        [Fact]
        public void TopLevelWithExpression()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main(List<int> list)
                    {
                        [] with { Count = 1, };
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,9): error CS9503: There is no target type for the collection expression.
                //         [] with { Count = 1, };
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 9),
                // (7,9): error CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement
                //         [] with { Count = 1, };
                Diagnostic(ErrorCode.ERR_IllegalStatement, "[] with { Count = 1, }").WithLocation(7, 9));
        }

        [Fact]
        public void TopLevelIsExpressions()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main(List<int> list)
                    {
                        [] is object;
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,9): error CS9503: There is no target type for the collection expression.
                //         [] is object;
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 9),
                // (7,9): error CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement
                //         [] is object;
                Diagnostic(ErrorCode.ERR_IllegalStatement, "[] is object").WithLocation(7, 9));
        }

        [Fact]
        public void TopLevelAsExpressions()
        {
            string source = """
                using System.Collections.Generic;

                class Program
                {
                    static void Main(List<int> list)
                    {
                        [] as List<int>;
                    }
                }
                """;
            CreateCompilation(source).VerifyEmitDiagnostics(
                // (7,9): error CS9503: There is no target type for the collection expression.
                //         [] as List<int>;
                Diagnostic(ErrorCode.ERR_CollectionExpressionNoTargetType, "[]").WithLocation(7, 9),
                // (7,9): error CS0201: Only assignment, call, increment, decrement, await, and new object expressions can be used as a statement
                //         [] as List<int>;
                Diagnostic(ErrorCode.ERR_IllegalStatement, "[] as List<int>").WithLocation(7, 9));
        }
    }
}
