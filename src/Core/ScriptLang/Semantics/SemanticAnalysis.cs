﻿#nullable enable
namespace ScTools.ScriptLang.Semantics
{
    using System;
    using System.Linq;

    using ScTools.ScriptLang.Ast;
    using ScTools.ScriptLang.Semantics.Binding;
    using ScTools.ScriptLang.Semantics.Symbols;

    public static partial class SemanticAnalysis
    {
        public static void DoFirstPass(Root root, string filePath, SymbolTable symbols, IUsingModuleResolver? usingResolver, DiagnosticsReport diagnostics)
            => new FirstPass(diagnostics, filePath, symbols, usingResolver).Run(root);

        public static void DoSecondPass(Root root, string filePath, SymbolTable symbols, DiagnosticsReport diagnostics)
            => new SecondPass(diagnostics, filePath, symbols).Run(root);

        public static BoundModule DoBinding(Root root, string filePath, SymbolTable symbols, DiagnosticsReport diagnostics)
        {
            var pass = new Binder(diagnostics, filePath, symbols);
            pass.Run(root);
            return pass.Module;
        }

        private abstract class Pass : AstVisitor
        {
            public DiagnosticsReport Diagnostics { get; set; }
            public string FilePath { get; set; }
            public SymbolTable Symbols { get; set; }

            public Pass(DiagnosticsReport diagnostics, string filePath, SymbolTable symbols)
                => (Diagnostics, FilePath, Symbols) = (diagnostics, filePath, symbols);

            public void Run(Root root)
            {
                root.Accept(this);
                OnEnd();
            }

            protected virtual void OnEnd() { }

            protected Type TryResolveVarDecl(VariableDeclaration varDecl)
            {
                bool unresolved = false;
                return Resolve(TypeFromAst(varDecl.Type, varDecl.Decl), varDecl.Source, ref unresolved);
            }

            protected Type TypeFromAst(string type, Declarator? decl)
            {
                var unresolved = new UnresolvedType(type);
                return decl != null ? TypeFromDecl(decl, unresolved) : unresolved;
            }

            private static Type TypeFromDecl(Declarator decl, Type baseType)
            {
                var ty = baseType;
                while (decl is not SimpleDeclarator)
                {
                    switch (decl)
                    {
                        case SimpleDeclarator: break;
                        case ArrayDeclarator d:
                            var lengthExpr = new ExpressionBinder().Visit(d.Length)!;
                            var length = Evaluator.Evaluate(lengthExpr)[0].AsInt32;
                            ty = new ArrayType(ty, length);
                            decl = d.Inner;
                            break;
                        case RefDeclarator d:
                            ty = new RefType(ty);
                            decl = d.Inner;
                            break;
                        default: throw new NotImplementedException();
                    };
                }

                return ty;
            }

            protected void ResolveStruct(StructType struc, SourceRange source, ref bool unresolved)
            {
                // TODO: be more specific with SourceRange for structs fields
                for (int i = 0; i < struc.Fields.Count; i++)
                {
                    var f = struc.Fields[i];
                    var newType = Resolve(f.Type, source, ref unresolved);
                    if (IsCyclic(newType, struc))
                    {
                        Diagnostics.AddError(FilePath, $"Circular type reference in '{struc.Name}'", source);
                        unresolved = true;
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

            protected void ResolveFunc(FunctionType func, SourceRange source, ref bool unresolved)
            {
                // TODO: be more specific with SourceRange for funcs return type and parameters
                if (func.ReturnType != null)
                {
                    func.ReturnType = Resolve(func.ReturnType, source, ref unresolved);
                }

                for (int i = 0; i < func.Parameters.Count; i++)
                {
                    var p = func.Parameters[i];
                    func.Parameters[i] = (Resolve(p.Type, source, ref unresolved), p.Name);
                }
            }

            protected void ResolveArray(ArrayType arr, SourceRange source, ref bool unresolved)
            {
                arr.ItemType = Resolve(arr.ItemType, source, ref unresolved);
            }

            protected void ResolveRef(RefType refTy, SourceRange source, ref bool unresolved)
            {
                refTy.ElementType = Resolve(refTy.ElementType, source, ref unresolved);
            }

            protected Type Resolve(Type t, SourceRange source, ref bool unresolved)
            {
                switch (t)
                {
                    case UnresolvedType ty:
                    {
                        var newType = ty.Resolve(Symbols);
                        if (newType == null)
                        {
                            Diagnostics.AddError(FilePath, $"Unknown type '{ty.TypeName}'", source);
                            unresolved = true;
                            return ty;
                        }
                        else
                        {
                            return newType;
                        }
                    }
                    case RefType ty: ResolveRef(ty, source, ref unresolved); return ty;
                    case StructType ty: ResolveStruct(ty, source, ref unresolved); return ty;
                    case FunctionType ty: ResolveFunc(ty, source, ref unresolved); return ty;
                    case ArrayType ty: ResolveArray(ty, source, ref unresolved); return ty;
                    default: return t;
                }
            }

            protected Type? TypeOf(Expression expr) => expr.Accept(new TypeOf(Diagnostics, FilePath, Symbols));
        }
    }
}
