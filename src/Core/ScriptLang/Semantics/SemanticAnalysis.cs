﻿#nullable enable
namespace ScTools.ScriptLang.Semantics
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    using ScTools.ScriptLang.Ast;
    using ScTools.ScriptLang.Semantics.Symbols;

    public static class SemanticAnalysis
    {
        public static (DiagnosticsReport, SymbolTable) Visit(Root root, string filePath)
        {
            var diagnostics = new DiagnosticsReport();
            var symbols = new SymbolTable();
            AddBuiltIns(symbols);
            var pass1 = new FirstPass(diagnostics, filePath, symbols);
            root.Accept(pass1);
            bool allTypesInGlobalScopeResolved = pass1.ResolveTypes();

            var pass2 = new SecondPass(diagnostics, filePath, symbols);
            root.Accept(pass2);

            return (diagnostics, symbols);
        }

        private static void AddBuiltIns(SymbolTable symbols)
        {
            var fl = new BasicType(BasicTypeCode.Float);
            symbols.Add(new TypeSymbol("INT", SourceRange.Unknown, new BasicType(BasicTypeCode.Int)));
            symbols.Add(new TypeSymbol("FLOAT", SourceRange.Unknown, fl));
            symbols.Add(new TypeSymbol("BOOL", SourceRange.Unknown, new BasicType(BasicTypeCode.Bool)));
            symbols.Add(new TypeSymbol("STRING", SourceRange.Unknown, new BasicType(BasicTypeCode.String)));
            symbols.Add(new TypeSymbol("VEC3", SourceRange.Unknown, new StructType("VEC3",
                                                                                    new Field(fl, "x"),
                                                                                    new Field(fl, "y"),
                                                                                    new Field(fl, "z"))));
        }

        /// <summary>
        /// Register global symbols (structs, static variable, procedures and functions)
        /// </summary>
        private sealed class FirstPass : AstVisitor
        {
            public DiagnosticsReport Diagnostics { get; set; }
            public string FilePath { get; set; }
            public SymbolTable Symbols { get; set; }

            public FirstPass(DiagnosticsReport diagnostics, string filePath, SymbolTable symbols)
                => (Diagnostics, FilePath, Symbols) = (diagnostics, filePath, symbols);

            // returns whether all types where resolved
            public bool ResolveTypes()
            {
                bool anyUnresolved = false;

                foreach (var symbol in Symbols.Symbols)
                {
                    switch (symbol)
                    {
                        case VariableSymbol s: s.Type = Resolve(s.Type, s.Source); break;
                        case FunctionSymbol s: ResolveFunc(s.Type, s.Source); break;
                        case TypeSymbol s when s.Type is StructType struc: ResolveStruct(struc, s.Source); break;
                        case TypeSymbol s when s.Type is FunctionType func: ResolveFunc(func, s.Source); break;
                    }
                }

                return !anyUnresolved;

                void ResolveStruct(StructType struc, SourceRange source)
                {
                    // TODO: be more specific with SourceRange for structs fields
                    for (int i = 0; i < struc.Fields.Count; i++)
                    {
                        var f = struc.Fields[i];
                        var newType = Resolve(f.Type, source);
                        if (IsCyclic(newType, struc))
                        {
                            Diagnostics.AddError(FilePath, $"Circular type reference in '{struc.Name}'", source);
                            anyUnresolved |= true;
                        }
                        else
                        {
                            struc.Fields[i] = new Field(newType, f.Name);
                        }
                    }

                    static bool IsCyclic(Type t, StructType orig)
                    {
                        if (t == orig)
                        {
                            return true;
                        }
                        else if (t is StructType s)
                        {
                            return s.Fields.Any(f => IsCyclic(f.Type, orig));
                        }

                        return false;
                    }
                }

                void ResolveFunc(FunctionType func, SourceRange source)
                {
                    // TODO: be more specific with SourceRange for funcs return type and parameters
                    if (func.ReturnType != null)
                    {
                        func.ReturnType = Resolve(func.ReturnType, source);
                    }
 
                    for (int i = 0; i < func.Parameters.Count; i++)
                    {
                        func.Parameters[i] = Resolve(func.Parameters[i], source);
                    }
                }

                Type Resolve(Type t, SourceRange source)
                {
                    if (t is UnresolvedType u)
                    {
                        var newType = u.Resolve(Symbols);
                        if (newType == null)
                        {
                            Diagnostics.AddError(FilePath, $"Unknown type '{u.TypeName}'", source);
                            anyUnresolved |= true;
                        }
                        else
                        {
                            return newType;
                        }
                    }

                    return t;
                }
            }

            private FunctionType CreateUnresolvedFunctionType(Ast.Type? returnType, IEnumerable<VariableDeclaration> parameters)
            {
                var r = returnType != null ? new UnresolvedType(returnType.Name) : null;
                return new FunctionType(r, parameters.Select(p => new UnresolvedType(p.Type.Name)));
            }

            public override void VisitFunctionStatement(FunctionStatement node)
            {
                Symbols.Add(new FunctionSymbol(node.Name, node.Source, CreateUnresolvedFunctionType(node.ReturnType, node.ParameterList.Parameters)));
            }

            public override void VisitProcedureStatement(ProcedureStatement node)
            {
                Symbols.Add(new FunctionSymbol(node.Name, node.Source, CreateUnresolvedFunctionType(null, node.ParameterList.Parameters)));
            }

            public override void VisitFunctionPrototypeStatement(FunctionPrototypeStatement node)
            {
                var func = CreateUnresolvedFunctionType(node.ReturnType, node.ParameterList.Parameters);

                Symbols.Add(new TypeSymbol(node.Name, node.Source, func));
            }

            public override void VisitProcedurePrototypeStatement(ProcedurePrototypeStatement node)
            {
                var func = CreateUnresolvedFunctionType(null, node.ParameterList.Parameters);

                Symbols.Add(new TypeSymbol(node.Name, node.Source, func));
            }

            public override void VisitStaticVariableStatement(StaticVariableStatement node)
            {
                // TODO: allocate static variables
                Symbols.Add(new VariableSymbol(node.Variable.Declaration.Name,
                                               node.Source,
                                               new UnresolvedType(node.Variable.Declaration.Type.Name),
                                               VariableKind.Static));
            }

            public override void VisitStructStatement(StructStatement node)
            {
                var struc = new StructType(node.Name, node.FieldList.Fields.Select(f => new Field(new UnresolvedType(f.Declaration.Type.Name), f.Declaration.Name)));

                Symbols.Add(new TypeSymbol(node.Name, node.Source, struc));
            }

            public override void DefaultVisit(Node node)
            {
                foreach (var n in node.Children)
                {
                    n.Accept(this);
                }
            }
        }

        /// <summary>
        /// Register local symbols inside procedures/functions and check expression.
        /// </summary>
        private sealed class SecondPass : AstVisitor
        {
            public DiagnosticsReport Diagnostics { get; set; }
            public string FilePath { get; set; }
            public SymbolTable Symbols { get; set; }

            private int funcLocalsSize = 0;
            private int funcLocalArgsSize = 0; 
            private int funcAllocLocation = 0;

            public SecondPass(DiagnosticsReport diagnostics, string filePath, SymbolTable symbols)
                => (Diagnostics, FilePath, Symbols) = (diagnostics, filePath, symbols);

            public override void VisitFunctionStatement(FunctionStatement node)
            {
                var funcSymbol = Symbols.Lookup(node.Name) as FunctionSymbol;
                Debug.Assert(funcSymbol != null);

                VisitFunc(funcSymbol, node.ParameterList, node.Block);
            }

            public override void VisitProcedureStatement(ProcedureStatement node)
            {
                var funcSymbol = Symbols.Lookup(node.Name) as FunctionSymbol;
                Debug.Assert(funcSymbol != null);

                VisitFunc(funcSymbol, node.ParameterList, node.Block);
            }

            private void VisitFunc(FunctionSymbol func, ParameterList parameters, StatementBlock block)
            {
                funcLocalsSize = 0;
                funcLocalArgsSize = 0;
                funcAllocLocation = 0;

                Symbols = Symbols.EnterScope(block);
                parameters.Accept(this);
                block.Accept(this);
                Symbols = Symbols.ExitScope();

                func.LocalArgsSize = funcLocalArgsSize;
                func.LocalsSize = funcLocalsSize;
            }

            private Type TypeOf(string typeName, SourceRange source)
            {
                var unresolved = new UnresolvedType(typeName);
                var resolved = unresolved.Resolve(Symbols);
                if (resolved == null)
                {
                    Diagnostics.AddError(FilePath, $"Unknown type '{typeName}'", source);
                }

                return resolved ?? unresolved;
            }

            public override void VisitParameterList(ParameterList node)
            {
                foreach (var p in node.Parameters)
                {
                    var v = new VariableSymbol(p.Name,
                                               p.Source,
                                               TypeOf(p.Type.Name, p.Type.Source),
                                               VariableKind.LocalArgument)
                            {
                                Location = funcAllocLocation,
                            };
                    int size = v.Type.SizeOf;
                    funcAllocLocation += size;
                    funcLocalArgsSize += size;
                    Symbols.Add(v);
                }
                funcAllocLocation += 2; // space required by the game
            }

            public override void VisitVariableDeclarationStatement(VariableDeclarationStatement node)
            {
                var v = new VariableSymbol(node.Variable.Declaration.Name,
                                           node.Source,
                                           TypeOf(node.Variable.Declaration.Type.Name, node.Variable.Declaration.Type.Source),
                                           VariableKind.Local)
                {
                    Location = funcAllocLocation,
                };
                int size = v.Type.SizeOf;
                funcAllocLocation += size;
                funcLocalsSize += size;
                Symbols.Add(v);
            }

            public override void VisitIfStatement(IfStatement node)
            {
                node.Condition.Accept(this);

                Symbols = Symbols.EnterScope(node.ThenBlock);
                node.ThenBlock.Accept(this);
                Symbols = Symbols.ExitScope();

                if (node.ElseBlock != null)
                {
                    Symbols = Symbols.EnterScope(node.ElseBlock);
                    node.ElseBlock.Accept(this);
                    Symbols = Symbols.ExitScope();
                }
            }

            public override void VisitWhileStatement(WhileStatement node)
            {
                node.Condition.Accept(this);

                Symbols = Symbols.EnterScope(node.Block);
                node.Block.Accept(this);
                Symbols = Symbols.ExitScope();
            }

            public override void DefaultVisit(Node node)
            {
                foreach (var n in node.Children)
                {
                    n.Accept(this);
                }
            }
        }
    }
}
