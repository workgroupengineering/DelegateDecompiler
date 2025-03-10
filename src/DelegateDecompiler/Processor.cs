﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Reflection;
using System.Runtime.CompilerServices;

namespace DelegateDecompiler
{
    internal class Processor
    {
        const string cachedAnonymousMethodDelegate = "CS$<>9__CachedAnonymousMethodDelegate";
        const string cachedAnonymousMethodDelegateRoslyn = "<>9__";

        static readonly MethodInfo StringConcat = typeof(string).GetMethod("Concat", new[] { typeof(object), typeof(object) });

        //TODO: Move to ProcessorState??
        static readonly ConcurrentDictionary<MethodInfo, LambdaExpression> AnonymousDelegatesCache =
            new ConcurrentDictionary<MethodInfo, LambdaExpression>();

        public static Expression Process(VariableInfo[] locals, IList<Address> args, Instruction instruction, Type returnType)
        {
            Processor processor = new Processor();
            processor.states.Push(new ProcessorState(new Stack<Address>(), locals, args, instruction));

            var ex = AdjustType(processor.Process(), returnType);

            if (!returnType.IsAssignableFrom(ex.Type) && returnType != typeof(void))
            {
                return Expression.Convert(ex, returnType);
            }

            return ex;
        }

        readonly Stack<ProcessorState> states = new Stack<ProcessorState>();

        Processor()
        {
        }

        Expression Process()
        {
            ProcessorState state = null;
            while (states.Count > 0)
            {
                state = states.Peek();

                if (state.RunNext != null)
                {
                    state.RunNext();
                    state.RunNext = null;
                }

                if (state.Instruction != null && state.Instruction != state.Last)
                {
                    Debug.WriteLine(state.Instruction);

                    if (state.Instruction.OpCode == OpCodes.Nop || state.Instruction.OpCode == OpCodes.Break)
                    {
                        //do nothing;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldtoken)
                    {
                        var runtimeHandle = GetRuntimeHandle(state.Instruction.Operand);
                        state.Stack.Push(Expression.Constant(runtimeHandle));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldarg_0)
                    {
                        LdArg(state, 0);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldarg_1)
                    {
                        LdArg(state, 1);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldarg_2)
                    {
                        LdArg(state, 2);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldarg_3)
                    {
                        LdArg(state, 3);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldarg_S || state.Instruction.OpCode == OpCodes.Ldarg || state.Instruction.OpCode == OpCodes.Ldarga || state.Instruction.OpCode == OpCodes.Ldarga_S)
                    {
                        var operand = (ParameterInfo)state.Instruction.Operand;
                        state.Stack.Push(state.Args.Single(x => ((ParameterExpression)x.Expression).Name == operand.Name));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldlen)
                    {
                        var array = state.Stack.Pop();
                        state.Stack.Push(Expression.ArrayLength(array));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldelem ||
                             state.Instruction.OpCode == OpCodes.Ldelem_I ||
                             state.Instruction.OpCode == OpCodes.Ldelem_I1 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_I2 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_I4 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_I8 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_U1 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_U2 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_U4 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_R4 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_R8 ||
                             state.Instruction.OpCode == OpCodes.Ldelem_Ref)
                    {
                        var index = state.Stack.Pop();
                        var array = state.Stack.Pop();
                        state.Stack.Push(Expression.ArrayIndex(array, index));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stloc_0)
                    {
                        StLoc(state, 0);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stloc_1)
                    {
                        StLoc(state, 1);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stloc_2)
                    {
                        StLoc(state, 2);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stloc_3)
                    {
                        StLoc(state, 3);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stloc_S ||
                             state.Instruction.OpCode == OpCodes.Stloc)
                    {
                        var operand = (LocalVariableInfo)state.Instruction.Operand;
                        StLoc(state, operand.LocalIndex);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stelem ||
                             state.Instruction.OpCode == OpCodes.Stelem_I ||
                             state.Instruction.OpCode == OpCodes.Stelem_I1 ||
                             state.Instruction.OpCode == OpCodes.Stelem_I2 ||
                             state.Instruction.OpCode == OpCodes.Stelem_I4 ||
                             state.Instruction.OpCode == OpCodes.Stelem_I8 ||
                             state.Instruction.OpCode == OpCodes.Stelem_R4 ||
                             state.Instruction.OpCode == OpCodes.Stelem_R8 ||
                             state.Instruction.OpCode == OpCodes.Stelem_Ref)
                    {
                        StElem(state);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldnull)
                    {
                        state.Stack.Push(Expression.Constant(null));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldfld || state.Instruction.OpCode == OpCodes.Ldflda)
                    {
                        LdFld(state, state.Stack.Pop());
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldsfld)
                    {
                        LdFld(state, null);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stsfld)
                    {
                        var value = state.Stack.Pop();
                        var field = (FieldInfo)state.Instruction.Operand;
                        if (IsCachedAnonymousMethodDelegate(field))
                        {
                            state.Delegates[Tuple.Create(default(Address), field)] = value;
                        }
                        else
                        {
                            state.Stack.Push(Expression.Assign(Expression.Field(null, field), value));
                        }
                    }
                    else if (state.Instruction.OpCode == OpCodes.Stfld)
                    {
                        var value = state.Stack.Pop();
                        var instance = state.Stack.Pop();
                        var field = (FieldInfo)state.Instruction.Operand;
                        if (IsCachedAnonymousMethodDelegate(field))
                        {
                            state.Delegates[Tuple.Create(instance, field)] = value;
                        }
                        else
                        {
                            var expression = BuildAssignment(instance.Expression, field, value, out var push);
                            if (push)
                                state.Stack.Push(expression);
                            else
                                instance.Expression = expression;
                        }
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldloc_0)
                    {
                        LdLoc(state, 0);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldloc_1)
                    {
                        LdLoc(state, 1);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldloc_2)
                    {
                        LdLoc(state, 2);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldloc_3)
                    {
                        LdLoc(state, 3);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldloc ||
                             state.Instruction.OpCode == OpCodes.Ldloc_S ||
                             state.Instruction.OpCode == OpCodes.Ldloca ||
                             state.Instruction.OpCode == OpCodes.Ldloca_S)
                    {
                        var operand = (LocalVariableInfo)state.Instruction.Operand;
                        LdLoc(state, operand.LocalIndex);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldstr)
                    {
                        state.Stack.Push(Expression.Constant((string)state.Instruction.Operand));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_0)
                    {
                        LdC(state, 0);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_1)
                    {
                        LdC(state, 1);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_2)
                    {
                        LdC(state, 2);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_3)
                    {
                        LdC(state, 3);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_4)
                    {
                        LdC(state, 4);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_5)
                    {
                        LdC(state, 5);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_6)
                    {
                        LdC(state, 6);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_7)
                    {
                        LdC(state, 7);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_8)
                    {
                        LdC(state, 8);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_S)
                    {
                        LdC(state, (sbyte)state.Instruction.Operand);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4_M1)
                    {
                        LdC(state, -1);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I4)
                    {
                        LdC(state, (int)state.Instruction.Operand);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_I8)
                    {
                        LdC(state, (long)state.Instruction.Operand);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_R4)
                    {
                        LdC(state, (float)state.Instruction.Operand);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldc_R8)
                    {
                        LdC(state, (double)state.Instruction.Operand);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Br_S || state.Instruction.OpCode == OpCodes.Br)
                    {
                        state.Instruction = (Instruction)state.Instruction.Operand;
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Brfalse ||
                             state.Instruction.OpCode == OpCodes.Brfalse_S)
                    {
                        state.Instruction = ConditionalBranch(state, val => Expression.Equal(val, ExpressionHelper.Default(val.Type)));
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Brtrue ||
                             state.Instruction.OpCode == OpCodes.Brtrue_S)
                    {
                        var address = state.Stack.Peek();
                        var memberExpression = address.Expression as MemberExpression;
                        if (memberExpression != null && IsCachedAnonymousMethodDelegate(memberExpression.Member as FieldInfo))
                        {
                            state.Stack.Pop();
                        }
                        else
                        {
                            state.Instruction = ConditionalBranch(state, val => val.Type == typeof(bool) ? val : Expression.NotEqual(val, ExpressionHelper.Default(val.Type)));
                            continue;
                        }
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ldftn)
                    {
                        var method = (MethodInfo)state.Instruction.Operand;
                        state.Stack.Push(DecompileLambdaExpression(method, () => state.Stack.Pop()));
                        state.Instruction = state.Instruction.Next;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Bgt ||
                             state.Instruction.OpCode == OpCodes.Bgt_S ||
                             state.Instruction.OpCode == OpCodes.Bgt_Un ||
                             state.Instruction.OpCode == OpCodes.Bgt_Un_S)
                    {
                        var val1 = state.Stack.Pop();
                        state.Instruction = ConditionalBranch(state, val => MakeBinaryExpression(val, val1, ExpressionType.GreaterThan));
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Bge ||
                             state.Instruction.OpCode == OpCodes.Bge_S ||
                             state.Instruction.OpCode == OpCodes.Bge_Un ||
                             state.Instruction.OpCode == OpCodes.Bge_Un_S)
                    {
                        var val1 = state.Stack.Pop();
                        state.Instruction = ConditionalBranch(state, val => MakeBinaryExpression(val, val1, ExpressionType.GreaterThanOrEqual));
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Blt ||
                             state.Instruction.OpCode == OpCodes.Blt_S ||
                             state.Instruction.OpCode == OpCodes.Blt_Un ||
                             state.Instruction.OpCode == OpCodes.Blt_Un_S)
                    {
                        var val1 = state.Stack.Pop();
                        state.Instruction = ConditionalBranch(state, val => MakeBinaryExpression(val, val1, ExpressionType.LessThan));
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ble ||
                             state.Instruction.OpCode == OpCodes.Ble_S ||
                             state.Instruction.OpCode == OpCodes.Ble_Un ||
                             state.Instruction.OpCode == OpCodes.Ble_Un_S)
                    {
                        var val1 = state.Stack.Pop();
                        state.Instruction = ConditionalBranch(state, val => MakeBinaryExpression(val, val1, ExpressionType.LessThanOrEqual));
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Beq ||
                             state.Instruction.OpCode == OpCodes.Beq_S)
                    {
                        var val1 = state.Stack.Pop();
                        state.Instruction = ConditionalBranch(state, val => MakeBinaryExpression(val, val1, ExpressionType.Equal));
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Bne_Un ||
                             state.Instruction.OpCode == OpCodes.Bne_Un_S)
                    {
                        var val1 = state.Stack.Pop();
                        state.Instruction = ConditionalBranch(state, val => MakeBinaryExpression(val, val1, ExpressionType.NotEqual));
                        continue;
                    }
                    else if (state.Instruction.OpCode == OpCodes.Dup)
                    {
                        state.Stack.Push(state.Stack.Peek());
                    }
                    else if (state.Instruction.OpCode == OpCodes.Pop)
                    {
                        state.Stack.Pop();
                    }
                    else if (state.Instruction.OpCode == OpCodes.Add)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.Add));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Add_Ovf || state.Instruction.OpCode == OpCodes.Add_Ovf_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.AddChecked));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Sub)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.Subtract));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Sub_Ovf || state.Instruction.OpCode == OpCodes.Sub_Ovf_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.SubtractChecked));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Mul)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.Multiply));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Mul_Ovf || state.Instruction.OpCode == OpCodes.Mul_Ovf_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.MultiplyChecked));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Div || state.Instruction.OpCode == OpCodes.Div_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.Divide));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Rem || state.Instruction.OpCode == OpCodes.Rem_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.Modulo));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Xor)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.ExclusiveOr));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Shl)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.LeftShift));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Shr || state.Instruction.OpCode == OpCodes.Shr_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.RightShift));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Neg)
                    {
                        var val = state.Stack.Pop();
                        state.Stack.Push(Expression.Negate(val));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Not)
                    {
                        var val = state.Stack.Pop();
                        state.Stack.Push(MakeUnaryExpression(val, ExpressionType.Not));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_I)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(int))); // Support x64?
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_I1)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(sbyte)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_I2)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(short)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_I4)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(int)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_I8)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(long)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_U)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(uint))); // Suppot x64?
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_U1)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(byte)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_U2)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(ushort)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_U4)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(uint)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_U8)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, typeof(ulong)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_I || state.Instruction.OpCode == OpCodes.Conv_Ovf_I_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(int))); // Suppot x64?
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_I1 || state.Instruction.OpCode == OpCodes.Conv_Ovf_I1_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(sbyte)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_I2 || state.Instruction.OpCode == OpCodes.Conv_Ovf_I2_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(short)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_I4 || state.Instruction.OpCode == OpCodes.Conv_Ovf_I4_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(int)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_I8 || state.Instruction.OpCode == OpCodes.Conv_Ovf_I8_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(long)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_U || state.Instruction.OpCode == OpCodes.Conv_Ovf_U_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(uint))); // Suppot x64?
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_U1 || state.Instruction.OpCode == OpCodes.Conv_Ovf_U1_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(byte)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_U2 || state.Instruction.OpCode == OpCodes.Conv_Ovf_U2_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(ushort)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_U4 || state.Instruction.OpCode == OpCodes.Conv_Ovf_U4_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(uint)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_Ovf_U8 || state.Instruction.OpCode == OpCodes.Conv_Ovf_U8_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(ulong)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_R4 || state.Instruction.OpCode == OpCodes.Conv_R_Un)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(float)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Conv_R8)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.ConvertChecked(val1, typeof(double)));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Castclass)
                    {
                        var val1 = state.Stack.Pop();
                        state.Stack.Push(Expression.Convert(val1, (Type)state.Instruction.Operand));
                    }
                    else if (state.Instruction.OpCode == OpCodes.And)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.And));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Or)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();
                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.Or));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Initobj)
                    {
                        var address = state.Stack.Pop();
                        var type = (Type)state.Instruction.Operand;
                        address.Expression = ExpressionHelper.Default(type);
                    }
                    else if (state.Instruction.OpCode == OpCodes.Newarr)
                    {
                        var operand = (Type)state.Instruction.Operand;
                        var expression = state.Stack.Pop();
                        if (expression.Expression is ConstantExpression size && (int)size.Value == 0) // optimization
                            state.Stack.Push(Expression.NewArrayInit(operand));
                        else
                            state.Stack.Push(Expression.NewArrayBounds(operand, expression));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Box)
                    {
                        state.Stack.Push(Box(state.Stack.Pop(), (Type)state.Instruction.Operand));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Newobj)
                    {
                        var constructor = (ConstructorInfo)state.Instruction.Operand;
                        var arguments = GetArguments(state, constructor);
                        if (constructor.DeclaringType.IsNullableType() && constructor.GetParameters().Length == 1)
                        {
                            state.Stack.Push(Expression.Convert(arguments[0], constructor.DeclaringType));
                        }
                        else
                        {
                            state.Stack.Push(Expression.New(constructor, arguments));
                        }
                    }
                    else if (state.Instruction.OpCode == OpCodes.Call || state.Instruction.OpCode == OpCodes.Callvirt)
                    {
                        var method = state.Instruction.Operand as MethodInfo;
                        var constructor = state.Instruction.Operand as ConstructorInfo;
                        if (method != null)
                        {
                            Call(state, method);
                        }
                        else if (constructor != null)
                        {
                            var address = Expression.New(constructor, GetArguments(state, constructor));
                            var local = state.Stack.Pop();
                            local.Expression = address;
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ceq)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();

                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.Equal));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Cgt)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();

                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.GreaterThan));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Cgt_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();

                        var constantExpression = val1.Expression as ConstantExpression;
                        if (constantExpression != null && (constantExpression.Value as int? == 0 || constantExpression.Value == null))
                        {
                            //Special case.
                            state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.NotEqual));
                        }
                        else
                        {
                            state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.GreaterThan));
                        }
                    }
                    else if (state.Instruction.OpCode == OpCodes.Clt || state.Instruction.OpCode == OpCodes.Clt_Un)
                    {
                        var val1 = state.Stack.Pop();
                        var val2 = state.Stack.Pop();

                        state.Stack.Push(MakeBinaryExpression(val2, val1, ExpressionType.LessThan));
                    }
                    else if (state.Instruction.OpCode == OpCodes.Isinst)
                    {
                        var val = state.Stack.Pop();
                        if (state.Instruction.Next != null && state.Instruction.Next.OpCode == OpCodes.Ldnull &&
                            state.Instruction.Next.Next != null && state.Instruction.Next.Next.OpCode == OpCodes.Cgt_Un)
                        {
                            state.Stack.Push(Expression.TypeIs(val, (Type)state.Instruction.Operand));
                            state.Instruction = state.Instruction.Next.Next;
                        }
                        else
                        {
                            state.Stack.Push(Expression.TypeAs(val, (Type)state.Instruction.Operand));
                        }
                    }
                    else if (state.Instruction.OpCode == OpCodes.Ret)
                    {
                        states.Pop();
                    }
                    else
                    {
                        Debug.WriteLine("Unhandled!!!");
                    }

                    state.Instruction = state.Instruction.Next;
                }
                else
                {
                    states.Pop();
                }
            }

            return state == null ? Expression.Empty() : state.Final();
        }

        static void LdFld(ProcessorState state, Address instance)
        {
            var field = (FieldInfo)state.Instruction.Operand;
            if (IsCachedAnonymousMethodDelegate(field) &&
                state.Delegates.TryGetValue(Tuple.Create(instance, field), out var address))
            {
                state.Stack.Push(address);
            }
            else
            {
                state.Stack.Push(Expression.Field(instance?.Expression, field));
            }
        }

        static LambdaExpression DecompileLambdaExpression(MethodInfo method, Func<Expression> @this)
        {
            if (method.IsStatic)
            {
                return AnonymousDelegatesCache.GetOrAdd(method, m => m.Decompile());
            }

            //Should always call.
            var expression = @this();
            return AnonymousDelegatesCache.GetOrAdd(method, m =>
            {
                var decompiled = m.Decompile();

                var expressions = new Dictionary<Expression, Expression>
                {
                    {decompiled.Parameters[0], expression}
                };

                var delegateType = Expression.GetDelegateType(GetDelegateParameterTypes(m));
                var body = new ReplaceExpressionVisitor(expressions).Visit(decompiled.Body);
                body = TransparentIdentifierRemovingExpressionVisitor.RemoveTransparentIdentifiers(body);
                return Expression.Lambda(delegateType, body, decompiled.Parameters.Skip(1));
            });
        }

        static Type[] GetDelegateParameterTypes(MethodInfo m)
        {
            var parameters = m.GetParameters();

            var result = new Type[parameters.Length + 1];
            var index = 0;
            while (index < parameters.Length)
            {
                result[index] = parameters[index].ParameterType;
                index++;
            }

            result[index] = m.ReturnType;

            return result;
        }

        static object GetRuntimeHandle(object operand) => operand switch
        {
            FieldInfo field => field.FieldHandle,
            MethodBase method => method.MethodHandle,
            Type type => type.TypeHandle,
            _ => null
        };

        static bool IsCachedAnonymousMethodDelegate(FieldInfo field)
        {
            if (field == null) return false;
            return field.Name.StartsWith(cachedAnonymousMethodDelegate) && Attribute.IsDefined(field, typeof(CompilerGeneratedAttribute), false) ||
                   field.Name.StartsWith(cachedAnonymousMethodDelegateRoslyn) && field.DeclaringType != null && Attribute.IsDefined(field.DeclaringType, typeof(CompilerGeneratedAttribute), false);
        }

        static BinaryExpression MakeBinaryExpression(Address left, Address right, ExpressionType expressionType)
        {
            var rightType = right.Type;
            var leftType = left.Type;

            left = AdjustBooleanConstant(left, rightType);
            right = AdjustBooleanConstant(right, leftType);
            left = ConvertEnumExpressionToUnderlyingType(left);
            right = ConvertEnumExpressionToUnderlyingType(right);

            return Expression.MakeBinary(expressionType, left, right);
        }

        static UnaryExpression MakeUnaryExpression(Expression operand, ExpressionType expressionType)
        {
            operand = ConvertEnumExpressionToUnderlyingType(operand);

            return Expression.MakeUnary(expressionType, operand, operand.Type);
        }

        static Expression Box(Expression expression, Type type)
        {
            if (expression.Type == type)
                return expression;

            var constantExpression = expression as ConstantExpression;
            if (constantExpression != null)
            {
                if (type.IsEnum)
                    return Expression.Constant(Enum.ToObject(type, constantExpression.Value));
            }

            if (expression.Type.IsEnum)
                return Expression.Convert(expression, type);

            return expression;
        }

        static Expression ConvertEnumExpressionToUnderlyingType(Expression expression)
        {
            if (expression.Type.IsEnum)
                return Expression.Convert(expression, expression.Type.GetEnumUnderlyingType());

            return expression;
        }

        Instruction ConditionalBranch(ProcessorState state, Func<Expression, Expression> condition)
        {
            var val1 = state.Stack.Pop();
            var test = condition(val1);

            var left = (Instruction)state.Instruction.Operand;
            var right = state.Instruction.Next;

            var common = GetJointPoint(state.Instruction);

            var rightState = state.Clone(right, common);
            var leftState = state.Clone(left, common);
            states.Push(rightState);
            states.Push(leftState);

            // Run this once the conditional branches have been processed
            state.RunNext = () => state.Merge(test, leftState, rightState);

            return common;
        }

        static Instruction GetJointPoint(Instruction instruction)
        {
            var leftInstructions = FollowGraph(instruction.Next);
            var rightInstructions = FollowGraph((Instruction)instruction.Operand);

            Instruction common = null;
            foreach (var leftInstruction in leftInstructions)
            {
                if (rightInstructions.Count > 0 && leftInstruction == rightInstructions.Pop())
                    common = leftInstruction;
                else
                    break;
            }
            return common;
        }

        static Stack<Instruction> FollowGraph(Instruction instruction)
        {
            var instructions = new Stack<Instruction>();
            while (instruction != null)
            {
                instructions.Push(instruction);
                instruction = GetNextInstruction(instruction);
            }
            return instructions;
        }

        static Instruction GetNextInstruction(Instruction instruction)
        {
            switch (instruction.OpCode.FlowControl)
            {
                case FlowControl.Return:
                    return null;
                case FlowControl.Branch:
                    return (Instruction)instruction.Operand;
                case FlowControl.Cond_Branch:
                    return GetJointPoint(instruction);
                default:
                    return instruction.Next;
            }
        }

        internal static Expression AdjustType(Expression expression, Type type)
        {
            if (expression.Type == type)
            {
                return expression;
            }

            if (expression is ConstantExpression constant)
            {
                if (constant.Value == null)
                {
                    return Expression.Constant(null, type);
                }

                if (constant.Type == typeof(int))
                {
                    if (type.IsEnum)
                    {
                        return Expression.Constant(Enum.ToObject(type, constant.Value));
                    }

                    if (type == typeof(bool))
                    {
                        return Expression.Constant(Convert.ToBoolean(constant.Value));
                    }

                    if (type == typeof(byte))
                    {
                        return Expression.Constant(Convert.ToByte(constant.Value));
                    }

                    if (type == typeof(sbyte))
                    {
                        return Expression.Constant(Convert.ToSByte(constant.Value));
                    }

                    if (type == typeof(short))
                    {
                        return Expression.Constant(Convert.ToInt16(constant.Value));
                    }

                    if (type == typeof(ushort))
                    {
                        return Expression.Constant(Convert.ToUInt16(constant.Value));
                    }

                    if (type == typeof(uint))
                    {
                        return Expression.Constant(Convert.ToUInt32(constant.Value));
                    }
                }
            }
            else
            {
                if (expression.Type == typeof(int) && type == typeof(bool))
                {
                    return Expression.NotEqual(expression, Expression.Constant(0));
                }
            }

            if (!type.IsAssignableFrom(expression.Type) && expression.Type.IsEnum && expression.Type.GetEnumUnderlyingType() == type)
            {
                return Expression.Convert(expression, type);
            }

            if (type.IsValueType != expression.Type.IsValueType)
            {
                return Expression.Convert(expression, type);
            }

            return expression;
        }

        static Expression AdjustBooleanConstant(Expression expression, Type type)
        {
            if (type == typeof(bool) && expression.Type == typeof(int))
            {
                var constantExpression = expression as ConstantExpression;
                if (constantExpression != null)
                {
                    return Expression.Constant(!Equals(constantExpression.Value, 0));
                }
            }

            return expression;
        }

        static void StElem(ProcessorState state)
        {
            var value = state.Stack.Pop();
            var index = state.Stack.Pop();
            var array = state.Stack.Pop();

            if (array.Expression is NewArrayExpression newArray)
            {
                var elementType = array.Type.GetElementType();
                var expressions = CreateArrayInitExpressions(elementType, newArray, value, index);
                array.Expression = Expression.NewArrayInit(elementType, expressions);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        static IEnumerable<Expression> CreateArrayInitExpressions(
            Type elementType, NewArrayExpression newArray, Expression valueExpression, Expression indexExpression)
        {
            var indexGetter = (Func<int>)Expression.Lambda(indexExpression).Compile();
            var index = indexGetter();

            Expression[] expressions;
            if (newArray.NodeType == ExpressionType.NewArrayInit)
            {
                expressions = newArray.Expressions.ToArray();
                if (index >= newArray.Expressions.Count)
                {
                    Array.Resize(ref expressions, index + 1);
                }

            }
            else if (newArray.NodeType == ExpressionType.NewArrayBounds)
            {
                var sizeExpression = newArray.Expressions.Single();
                var sizeGetter = (Func<int>)Expression.Lambda(sizeExpression).Compile();
                var getter = sizeGetter();

                expressions = Enumerable.Repeat(ExpressionHelper.Default(elementType), getter).ToArray();
            }
            else
            {
                throw new NotSupportedException();
            }

            expressions[index] = AdjustType(valueExpression, elementType);
            return expressions;
        }

        static void LdC(ProcessorState state, int i)
        {
            state.Stack.Push(Expression.Constant(i));
        }

        static void LdC(ProcessorState state, long i)
        {
            state.Stack.Push(Expression.Constant(i));
        }

        static void LdC(ProcessorState state, float i)
        {
            state.Stack.Push(Expression.Constant(i));
        }

        static void LdC(ProcessorState state, double i)
        {
            state.Stack.Push(Expression.Constant(i));
        }

        static void Call(ProcessorState state, MethodInfo m)
        {
            var mArgs = GetArguments(state, m, out var addresses);

            var instance = m.IsStatic ? new Address() : state.Stack.Pop();
            var result = BuildMethodCallExpression(m, instance, mArgs, addresses);
            if (result.Type != typeof(void))
                state.Stack.Push(result);
        }

        static Expression BuildAssignment(Expression instance, MemberInfo member, Expression value, out bool push)
        {
            var adjustedValue = AdjustType(value, member.FieldOrPropertyType());

            if (instance.NodeType == ExpressionType.New)
            {
                push = false;
                return Expression.MemberInit((NewExpression)instance, Expression.Bind(member, adjustedValue));
            }

            if (instance.NodeType == ExpressionType.MemberInit)
            {
                var memberInitExpression = (MemberInitExpression)instance;
                push = false;
                return Expression.MemberInit(
                    memberInitExpression.NewExpression,
                    new List<MemberBinding>(memberInitExpression.Bindings)
                    {
                        Expression.Bind(member, adjustedValue)
                    });
            }

            if (instance.Type.IsValueType &&
                (instance.NodeType == ExpressionType.Parameter || instance.NodeType == ExpressionType.Constant))
            {
                push = false;
                return Expression.MemberInit(Expression.New(instance.Type), Expression.Bind(member, adjustedValue));
            }

            push = true;
            return Expression.Assign(Expression.MakeMemberAccess(instance, member), adjustedValue);
        }

        static Expression[] GetArguments(ProcessorState state, MethodBase m)
        {
            return GetArguments(state, m, out _);
        }

        static Expression[] GetArguments(ProcessorState state, MethodBase m, out Address[] addresses)
        {
            var parameterInfos = m.GetParameters();
            addresses = new Address[parameterInfos.Length];
            var args = new Expression[parameterInfos.Length];
            for (var i = parameterInfos.Length - 1; i >= 0; i--)
            {
                var argument = state.Stack.Pop();
                var parameter = parameterInfos[i];
                var parameterType = parameter.ParameterType;
                args[i] = AdjustType(argument, parameterType.IsByRef ? parameterType.GetElementType() : parameterType);
                addresses[i] = argument;
            }

            return args;
        }

        static Expression BuildMethodCallExpression(MethodInfo m, Address instance, Expression[] arguments, Address[] addresses)
        {
            if (m.Name == "Add" && instance.Expression != null && typeof(IEnumerable).IsAssignableFrom(instance.Type))
            {
                var newExpression = instance.Expression as NewExpression;
                if (newExpression != null)
                {
                    var init = Expression.ListInit(newExpression, Expression.ElementInit(m, arguments));
                    instance.Expression = init;
                    return Expression.Empty();
                }
                var initExpression = instance.Expression as ListInitExpression;
                if (initExpression != null)
                {
                    var initializers = initExpression.Initializers.ToList();
                    initializers.Add(Expression.ElementInit(m, arguments));
                    var init = Expression.ListInit(initExpression.NewExpression, initializers);
                    instance.Expression = init;
                    return Expression.Empty();
                }
            }
            if (m.IsSpecialName)
            {
                if (m.Name.StartsWith("get_") && arguments.Length == 0)
                {
                    return Expression.Property(instance, m);
                }

                if (m.Name.StartsWith("set_") && arguments.Length == 1)
                {
                    var value = arguments.Single();
                    var property = Expression.Property(instance, m).Member;
                    var expression = BuildAssignment(instance.Expression, property, value, out bool push);
                    if (push)
                    {
                        return expression;
                    }
                    else
                    {
                        instance.Expression = expression;
                        return Expression.Empty();
                    }
                }

                if (m.Name.StartsWith("op_"))
                {
                    ExpressionType type;
                    if (TryParseOperator(m, out type))
                    {
                        switch (arguments.Length)
                        {
                            case 1:
                                return Expression.MakeUnary(type, arguments[0], arguments[0].Type);
                            case 2:
                                return Expression.MakeBinary(type, arguments[0], arguments[1]);
                        }
                    }
                    else
                    {
                        switch (m.Name)
                        {
                            case "op_Increment":
                                return Expression.Add(arguments[0], Expression.Constant(Convert.ChangeType(1, arguments[0].Type)));
                            case "op_Decrement":
                                return Expression.Subtract(arguments[0], Expression.Constant(Convert.ChangeType(1, arguments[0].Type)));
                            case "op_Implicit":
                                return Expression.Convert(arguments[0], m.ReturnType, m);
                        }
                    }
                }
            }
            if (m.IsStatic && m.DeclaringType == typeof(decimal))
            {
                switch (m.Name)
                {
                    case "Add":
                        return Expression.MakeBinary(ExpressionType.Add, arguments[0], arguments[1]);
                    case "Subtract":
                        return Expression.MakeBinary(ExpressionType.Subtract, arguments[0], arguments[1]);
                    case "Multiply":
                        return Expression.MakeBinary(ExpressionType.Multiply, arguments[0], arguments[1]);
                    case "Divide":
                        return Expression.MakeBinary(ExpressionType.Divide, arguments[0], arguments[1]);
                }
            }
            if (m.Name == "Concat" && m.DeclaringType == typeof(string))
            {
                var expressions = GetExpressionsForStringConcat(arguments);
                if (expressions.Count > 1)
                {
                    var expression = expressions[0];
                    for (var i = 1; i < expressions.Count; i++)
                    {
                        expression = Expression.Add(expression, expressions[i], StringConcat);
                    }
                    return expression;
                }
            }

            if (m.Name == "InitializeArray" && m.DeclaringType == typeof(RuntimeHelpers))
            {
                var arrayGetter = (Func<Array>)Expression.Lambda(arguments[0]).Compile();
                var fieldGetter = (Func<RuntimeFieldHandle>)Expression.Lambda(arguments[1]).Compile();
                var array = arrayGetter();
                RuntimeHelpers.InitializeArray(array, fieldGetter());

                IEnumerable<Expression> initializers = array.Cast<object>().Select(Expression.Constant);

                addresses[0].Expression = Expression.NewArrayInit(arguments[0].Type.GetElementType(), initializers);
                return Expression.Empty();
            }

            if (instance.Expression != null)
                return Expression.Call(instance, m, arguments);

            return Expression.Call(null, m, arguments);
        }

        static bool TryParseOperator(MethodInfo m, out ExpressionType type)
        {
            switch (m.Name)
            {
                /* The complete set of binary operator function names used is as follows: 
                 * op_Addition, op_Subtraction, op_Multiply, op_Division, op_Modulus, 
                 * op_BitwiseAnd, op_BitwiseOr, op_ExclusiveOr, op_LeftShift, op_RightShift, 
                 * op_Equality, op_Inequality, op_LessThan, op_LessThanOrEqual, op_GreaterThan, 
                 * and op_GreaterThanOrEqual.
                 */
                case "op_Addition":
                    type = ExpressionType.Add;
                    return true;

                case "op_Subtraction":
                    type = ExpressionType.Subtract;
                    return true;

                case "op_Multiply":
                    type = ExpressionType.Multiply;
                    return true;

                case "op_Division":
                    type = ExpressionType.Divide;
                    return true;

                case "op_Modulus":
                    type = ExpressionType.Modulo;
                    return true;

                case "op_BitwiseAnd":
                    type = ExpressionType.And;
                    return true;

                case "op_BitwiseOr":
                    type = ExpressionType.Or;
                    return true;

                case "op_ExclusiveOr":
                    type = ExpressionType.ExclusiveOr;
                    return true;

                case "op_LeftShift":
                    type = ExpressionType.LeftShift;
                    return true;

                case "op_RightShift":
                    type = ExpressionType.RightShift;
                    return true;

                case "op_Equality":
                    type = ExpressionType.Equal;
                    return true;

                case "op_Inequality":
                    type = ExpressionType.NotEqual;
                    return true;

                case "op_LessThan":
                    type = ExpressionType.LessThan;
                    return true;

                case "op_LessThanOrEqual":
                    type = ExpressionType.LessThanOrEqual;
                    return true;

                case "op_GreaterThan":
                    type = ExpressionType.GreaterThan;
                    return true;

                case "op_GreaterThanOrEqual":
                    type = ExpressionType.GreaterThanOrEqual;
                    return true;

                /*
                 * The complete set of unary operator function names used is as follows: 
                 * op_UnaryPlus, op_UnaryNegation, op_LogicalNot, op_OnesComplement, op_Increment, op_Decrement, op_True, and op_False.
                 */
                case "op_UnaryPlus":
                    type = ExpressionType.UnaryPlus;
                    return true;

                case "op_UnaryNegation":
                    type = ExpressionType.Negate;
                    return true;

                case "op_LogicalNot":
                    type = ExpressionType.Not;
                    return true;

                case "op_OnesComplement":
                    type = ExpressionType.OnesComplement;
                    return true;

                case "op_Increment":
                    type = default(ExpressionType);
                    return false;

                case "op_Decrement":
                    type = default(ExpressionType);
                    return false;

                case "op_True":
                    type = default(ExpressionType);
                    return false;

                case "op_False":
                    type = default(ExpressionType);
                    return false;
            }
            return Enum.TryParse(m.Name.Substring(3), out type);
        }

        static IList<Expression> GetExpressionsForStringConcat(Expression[] arguments)
        {
            if (arguments.Length == 1)
            {
                var array = arguments[0] as NewArrayExpression;
                if (array != null)
                {
                    if (array.NodeType == ExpressionType.NewArrayInit)
                    {
                        return array.Expressions;
                    }
                }
            }
            return arguments;
        }

        static void LdLoc(ProcessorState state, int index)
        {
            state.Stack.Push(state.Locals[index].Address);
        }

        static void StLoc(ProcessorState state, int index)
        {
            var info = state.Locals[index];
            var expression = AdjustType(state.Stack.Pop(), info.Type);
            info.Address = expression.Type == info.Type ? expression : Expression.Convert(expression, info.Type);
        }

        static void LdArg(ProcessorState state, int index)
        {
            state.Stack.Push(state.Args[index]);
        }
    }
}
