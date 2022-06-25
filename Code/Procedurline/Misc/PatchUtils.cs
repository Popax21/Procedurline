using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {
    public static class PatchUtils {
        [ThreadStatic]
        private static bool fromVirtualized;

        /// <summary>
        /// Virtualizes the given method.
        /// The method must be hiding a non-virtual method in the base class and have an empty/NOP body.
        /// After being patched, child classes overriding the class can call the original method by calling the base method, and their virtual method is called instead of the hidden method.
        /// </summary>
        public static void Virtualize(MethodInfo method, IList<IDetour> hooks) {
            //Get the hidden base method
            MethodInfo hiddenMethod = method.DeclaringType.GetMethod(method.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            //Check parameters
            ParameterInfo[] parameters = hiddenMethod.GetParameters();
            if(!parameters.SequenceEqual(method.GetParameters()) || method.ReturnParameter != hiddenMethod.ReturnParameter) throw new InvalidOperationException("Virtual method has different parameters than the base method!");

            //Install base method hook
            hooks.Add(new ILHook(hiddenMethod, ctx => {
                ILCursor cursor = new ILCursor(ctx);
                ILLabel notDeclareType = cursor.DefineLabel();
                FieldInfo fromVirtualizedField = typeof(PatchUtils).GetField(nameof(fromVirtualized), BindingFlags.NonPublic | BindingFlags.Static);

                //Check if the object is the declaring type
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Isinst);
                cursor.Emit(OpCodes.Ldsfld, fromVirtualizedField);
                cursor.Emit(OpCodes.Or);
                cursor.Emit(OpCodes.Brfalse, notDeclareType);

                //Call the virtual method
                cursor.Emit(OpCodes.Ldc_I4_0);
                cursor.Emit(OpCodes.Stsfld, fromVirtualizedField);

                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Castclass, method.DeclaringType);
                for(int i = 0; i < parameters.Length; i++) cursor.Emit(OpCodes.Ldarg, i);
                cursor.Emit(OpCodes.Callvirt, method);
                cursor.Emit(OpCodes.Ret);

                cursor.MarkLabel(notDeclareType);
            }));

            //Install virtual method hook
            hooks.Add(new ILHook(method, ctx => {
                ILCursor cursor = new ILCursor(ctx);
                FieldInfo fromVirtualizedField = typeof(PatchUtils).GetField(nameof(fromVirtualized), BindingFlags.NonPublic | BindingFlags.Static);
                ILLabel tryStart = cursor.DefineLabel(), tryEnd = cursor.DefineLabel(), finallyEnd = cursor.DefineLabel();

                //Call the base method
                cursor.MarkLabel(tryStart);
                cursor.Emit(OpCodes.Ldc_I4_1);
                cursor.Emit(OpCodes.Stsfld, fromVirtualizedField);

                cursor.Emit(OpCodes.Ldarg_0);
                for(int i = 0; i < parameters.Length; i++) cursor.Emit(OpCodes.Ldarg, i);
                cursor.Emit(OpCodes.Callvirt, hiddenMethod);
                cursor.Emit(OpCodes.Ret);
                cursor.MarkLabel(tryEnd);

                //Finally block
                cursor.Emit(OpCodes.Ldc_I4_0);
                cursor.Emit(OpCodes.Stsfld, fromVirtualizedField);
                cursor.Emit(OpCodes.Ret);
                cursor.MarkLabel(finallyEnd);

                ctx.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
                    TryStart = tryStart.Target,
                    TryEnd = tryEnd.Target,
                    HandlerStart = tryEnd.Target,
                    HandlerEnd = finallyEnd.Target
                });
            }));
        }
    }
}