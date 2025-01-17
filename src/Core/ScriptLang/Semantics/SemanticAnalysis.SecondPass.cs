﻿#nullable enable
namespace ScTools.ScriptLang.Semantics
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    using ScTools.ScriptLang.Ast;
    using ScTools.ScriptLang.Semantics.Symbols;

    public static partial class SemanticAnalysis
    {
        /// <summary>
        /// Register local symbols inside procedures/functions and check that expressions types are correct.
        /// </summary>
        private sealed class SecondPass : Pass
        {
            private DefinedFunctionSymbol? func = null;

            public SecondPass(DiagnosticsReport diagnostics, SymbolTable symbols)
                : base(diagnostics, symbols)
            { }

            protected override void OnEnd()
            {
                Debug.Assert(Symbols.Parent == null);
                Debug.Assert(Symbols.Symbols.Where(sym => sym is VariableSymbol)
                                            .Cast<VariableSymbol>()
                                            .All(s => s.Kind is VariableKind.Global or VariableKind.Static or VariableKind.Constant),
                             "All variables in global scope must be global, static or constant");
            }

            public override void VisitFunctionStatement(FunctionStatement node)
            {
                var funcSymbol = Symbols.Lookup(node.Name) as DefinedFunctionSymbol;
                Debug.Assert(funcSymbol != null);

                VisitFunc(funcSymbol, node.Parameters, node.Block);
            }

            public override void VisitProcedureStatement(ProcedureStatement node)
            {
                var funcSymbol = Symbols.Lookup(node.Name) as DefinedFunctionSymbol;
                Debug.Assert(funcSymbol != null);

                VisitFunc(funcSymbol, node.Parameters, node.Block);
            }

            private void VisitFunc(DefinedFunctionSymbol func, IEnumerable<Declaration> parameters, StatementBlock block)
            {
                this.func = func;

                Symbols = Symbols.EnterScope(block);
                VisitParameters(parameters);
                block.Accept(this);
                Symbols = Symbols.ExitScope();
            }

            public void VisitParameters(IEnumerable<Declaration> parameters)
            {
                foreach (var p in parameters)
                {
                    var v = new VariableSymbol(p.Declarator.Identifier,
                                               p.Source,
                                               ResolveTypeFromDecl(p),
                                               VariableKind.LocalArgument);
                    Symbols.Add(v);
                    func?.LocalArgs.Add(v);
                }
            }

            public override void VisitStaticVariableStatement(StaticVariableStatement node)
            {
                CheckGlobalOrStaticVariableType(node.Declaration);
            }

            public override void VisitConstantVariableStatement(ConstantVariableStatement node)
            {
                // empty
            }

            public override void VisitGlobalBlockStatement(GlobalBlockStatement node)
            {
                foreach (var decl in node.Variables)
                {
                    CheckGlobalOrStaticVariableType(decl);
                }
            }

            private void CheckGlobalOrStaticVariableType(Declaration decl)
            {
                var v = Symbols.Lookup(decl.Declarator.Identifier) as VariableSymbol;
                Debug.Assert(v != null);
                Debug.Assert(v.IsGlobal || v.IsStatic);

                if (v.Type is RefType)
                {
                    Diagnostics.AddError($"{(v.IsGlobal ? "Global" : "Static")} variables cannot be reference types", decl.Declarator.Source);
                }

                if (v.IsGlobal && v.Type is FunctionType)
                {
                    Diagnostics.AddError($"Global variables cannot be function/procedure types", decl.Declarator.Source);
                }

                if (decl.Initializer != null)
                {
                    if (v.Type is BasicType { TypeCode: BasicTypeCode.String })
                    {
                        Diagnostics.AddError($"{(v.IsGlobal ? "Global" : "Static")} variables of type STRING cannot have an initializer", decl.Initializer.Source);
                    }

                    var initializerType = TypeOf(decl.Initializer);
                    if (initializerType == null || !v.Type.IsAssignableFrom(initializerType, considerReferences: false))
                    {
                        Diagnostics.AddError($"Mismatched initializer type and type of {(v.IsGlobal ? "global" : "static")} variable '{v.Name}'", decl.Initializer.Source);
                    }
                }
            }

            public override void VisitVariableDeclarationStatement(VariableDeclarationStatement node)
            {
                var v = new VariableSymbol(node.Declaration.Declarator.Identifier,
                                           node.Source,
                                           ResolveTypeFromDecl(node.Declaration),
                                           VariableKind.Local);

                if (node.Declaration.Initializer != null)
                {
                    var initializerType = TypeOf(node.Declaration.Initializer);
                    if (initializerType == null || !v.Type.IsAssignableFrom(initializerType, considerReferences: true))
                    {
                        Diagnostics.AddError($"Mismatched initializer type and type of variable '{v.Name}'", node.Declaration.Initializer.Source);
                    }
                }

                Symbols.Add(v);
                func?.Locals.Add(v);
            }

            public override void VisitAssignmentStatement(AssignmentStatement node)
            {
                var destType = TypeOf(node.Left);
                var srcType = TypeOf(node.Right);
            
                if (destType == null || srcType == null)
                {
                    return;
                }

                if (!destType.IsAssignableFrom(srcType, considerReferences: true))
                {
                    Diagnostics.AddError("Mismatched types in assigment", node.Source);
                }

                if (destType is RefType refTy && refTy.ElementType is AnyType)
                {
                    Diagnostics.AddError("Cannot modify references of type ANY", node.Source);
                }
            }

            public override void VisitIfStatement(IfStatement node)
            {
                var conditionType = TypeOf(node.Condition);
                if (conditionType?.UnderlyingType is not BasicType { TypeCode: BasicTypeCode.Bool })
                {
                    Diagnostics.AddError($"IF statement condition requires BOOL type", node.Condition.Source);
                }

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
                var conditionType = TypeOf(node.Condition);
                if (conditionType?.UnderlyingType is not BasicType { TypeCode: BasicTypeCode.Bool })
                {
                    Diagnostics.AddError($"WHILE statement condition requires BOOL type", node.Condition.Source);
                }

                Symbols = Symbols.EnterScope(node.Block);
                node.Block.Accept(this);
                Symbols = Symbols.ExitScope();
            }

            public override void VisitRepeatStatement(RepeatStatement node)
            {
                var limitType = TypeOf(node.Limit);
                if (limitType?.UnderlyingType is not BasicType { TypeCode: BasicTypeCode.Int })
                {
                    Diagnostics.AddError($"REPEAT statement limit requires INT type", node.Limit.Source);
                }

                var counterType = TypeOf(node.Counter);
                if (counterType?.UnderlyingType is not BasicType { TypeCode: BasicTypeCode.Int })
                {
                    Diagnostics.AddError($"REPEAT statement counter requires INT type", node.Counter.Source);
                }

                Symbols = Symbols.EnterScope(node.Block);
                node.Block.Accept(this);
                Symbols = Symbols.ExitScope();
            }

            public override void VisitSwitchStatement(SwitchStatement node)
            {
                var exprType = TypeOf(node.Expression);
                if (exprType?.UnderlyingType is not BasicType { TypeCode: BasicTypeCode.Int })
                {
                    Diagnostics.AddError($"SWITCH statement value requires INT type", node.Expression.Source);
                }

                DefaultVisit(node);
            }

            public override void VisitValueSwitchCase(ValueSwitchCase node)
            {
                var valueType = TypeOf(node.Value);
                if (valueType is not BasicType { TypeCode: BasicTypeCode.Int })
                {
                    Diagnostics.AddError($"SWITCH case value requires INT type", node.Value.Source);
                }

                Symbols = Symbols.EnterScope(node.Block);
                node.Block.Accept(this);
                Symbols = Symbols.ExitScope();
            }

            public override void VisitDefaultSwitchCase(DefaultSwitchCase node)
            {
                Symbols = Symbols.EnterScope(node.Block);
                node.Block.Accept(this);
                Symbols = Symbols.ExitScope();
            }

            public override void VisitReturnStatement(ReturnStatement node)
            {
                Debug.Assert(func != null);

                if (node.Expression == null)
                {
                    // if this is a function, the missing expression should have been reported by SyntaxChecker
                    return;
                }

                var returnType = TypeOf(node.Expression);
                if (returnType == null || !func.Type.ReturnType!.IsAssignableFrom(returnType, considerReferences: true))
                {
                    Diagnostics.AddError($"Returned type does not match the specified function return type", node.Expression.Source);
                }
            }

            public override void VisitInvocationStatement(InvocationStatement node)
            {
                // TODO: very similar to TypeOf.VisitInvocationExpression, refactor
                var callableType = TypeOf(node.Expression);
                if (callableType is not FunctionType f)
                {
                    if (callableType != null)
                    {
                        Diagnostics.AddError($"Cannot call '{node.Expression}', it is not a procedure or a function", node.Expression.Source);
                    }
                    return;
                }

                int expected = f.ParameterCount;
                int found = node.Arguments.Length;
                if (found != expected)
                {
                    Diagnostics.AddError($"Mismatched number of arguments. Expected {expected}, found {found}", node.Source);
                }

                int argCount = Math.Min(expected, found);
                for (int i = 0; i < argCount; i++)
                {
                    var foundType = TypeOf(node.Arguments[i]);
                    if (!f.DoesParameterTypeMatch(i, foundType))
                    {
                        Diagnostics.AddError($"Mismatched type of argument #{i}", node.Arguments[i].Source);
                    }
                }
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
