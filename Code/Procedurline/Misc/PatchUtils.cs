using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {
    public static class PatchUtils {
        public const BindingFlags BindAllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        public const BindingFlags BindAllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public const BindingFlags BindAll = BindAllStatic | BindAllInstance;

        [ThreadStatic] private static bool fromVirtualized = false;

        /// <summary>
        /// Finds a field in this type or any base types recursively.
        /// </summary>
        public static FieldInfo GetFieldRecursive(this Type type, string name, BindingFlags flags) {
            if(type.GetField(name, flags) is FieldInfo info) return info;
            return type.BaseType?.GetFieldRecursive(name, flags);
        }

        /// <summary>
        /// Finds a property in this type or any base types recursively.
        /// </summary>
        public static PropertyInfo GetPropertyRecursive(this Type type, string name, BindingFlags flags) {
             if(type.GetProperty(name, flags) is PropertyInfo info) return info;
            return type.BaseType?.GetPropertyRecursive(name, flags);
        }

        /// <summary>
        /// Finds a method in this type or any base types recursively.
        /// </summary>
        public static MethodInfo GetMethodRecursive(this Type type, string name, BindingFlags flags, Type[] parameters = null) {
            if(parameters != null) {
                if(type.GetMethod(name, flags, null, parameters, null) is MethodInfo info) return info;
            } else {
                if(type.GetMethod(name, flags) is MethodInfo info) return info;
            }
            return type.BaseType?.GetMethodRecursive(name, flags, parameters);
        }

        /// <summary>
        /// Virtualizes the given method.
        /// The method must be hiding a non-virtual method in the base class and have an empty/NOP body, if the base method should still be invoked.
        /// After being patched, child classes overriding the class can call the original method by calling the base method, and their virtual method is called instead of the hidden method.
        /// </summary>
        public static void Virtualize(this MethodInfo method, bool callBase, IList<IDetour> hooks) {
            if(method.IsStatic) throw new ArgumentException("Can't virtualize a static method!");

            //Get the hidden base method
            Type[] parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();
            MethodInfo hiddenMethod = method.DeclaringType.BaseType.GetMethodRecursive(method.Name, BindAllInstance, parameters);

            //Install base method hook
            hooks.Add(new ILHook(hiddenMethod, ctx => {
                ILCursor cursor = new ILCursor(ctx);
                ILLabel notDeclareType = cursor.DefineLabel();
                FieldInfo fromVirtualizedField = typeof(PatchUtils).GetField(nameof(fromVirtualized), BindingFlags.NonPublic | BindingFlags.Static);

                //Check if the object is the declaring type
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Isinst, method.DeclaringType);
                cursor.Emit(OpCodes.Ldnull);
                cursor.Emit(OpCodes.Cgt_Un);
                cursor.Emit(OpCodes.Neg);
                cursor.Emit(OpCodes.Ldsfld, fromVirtualizedField);
                cursor.Emit(OpCodes.Neg);
                cursor.Emit(OpCodes.And);
                cursor.Emit(OpCodes.Brfalse, notDeclareType);

                //Call the virtual method
                cursor.Emit(OpCodes.Ldc_I4_0);
                cursor.Emit(OpCodes.Stsfld, fromVirtualizedField);

                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Castclass, method.DeclaringType);
                for(int i = 0; i < parameters.Length; i++) cursor.Emit(OpCodes.Ldarg, 1+i);
                cursor.Emit(OpCodes.Callvirt, method);
                cursor.Emit(OpCodes.Ret);

                cursor.MarkLabel(notDeclareType);
            }));

            if(callBase) {
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
                    cursor.Emit(OpCodes.Castclass, hiddenMethod.DeclaringType);
                    for(int i = 0; i < parameters.Length; i++) cursor.Emit(OpCodes.Ldarg, 1+i);
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

        /// <summary>
        /// Makes the property a field proxy. After patching, the property will proxy the given field instead of the compiler generate backing field.
        /// </summary>
        public static void MakeFieldProxy(this PropertyInfo prop, FieldInfo field, IList<IDetour> hooks) {
            //Check field for compatibility
            if(prop.GetAccessors(true).Any(a => a.IsStatic != field.IsStatic) || prop.PropertyType != field.FieldType) throw new ArgumentException("Mismatching field and property types!");
            if(!field.DeclaringType.IsAssignableFrom(prop.DeclaringType)) throw new ArgumentException("Property in different type than field!");

            //Hook getter
            if(prop.CanRead) {
                hooks.Add(new ILHook(prop.GetGetMethod(true), ctx => {
                    //Rip out the entire method body
                    ctx.Body.Instructions.Clear();

                    ILCursor cursor = new ILCursor(ctx);

                    //Get the field value
                    cursor.EmitReference(field);
                    if(field.IsStatic) cursor.Emit(OpCodes.Ldnull);
                    else cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Call, typeof(FieldInfo).GetMethod(nameof(FieldInfo.GetValue)));
                    cursor.Emit(OpCodes.Castclass, field.DeclaringType);
                    cursor.Emit(OpCodes.Ret);
                }));
            }

            //Hook setter
            if(prop.CanWrite) {
                hooks.Add(new ILHook(prop.GetSetMethod(true), ctx => {
                    //Rip out the entire method body
                    ctx.Body.Instructions.Clear();

                    ILCursor cursor = new ILCursor(ctx);

                    //Set the field value
                    cursor.EmitReference(field);
                    if(field.IsStatic) {
                        cursor.Emit(OpCodes.Ldnull);
                        cursor.Emit(OpCodes.Ldarg_0);
                    } else {
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.Emit(OpCodes.Ldarg_1);
                    }
                    cursor.Emit(OpCodes.Call, typeof(FieldInfo).GetMethod(nameof(FieldInfo.SetValue)));
                    cursor.Emit(OpCodes.Ret);
                }));
            }
        }
    }
}