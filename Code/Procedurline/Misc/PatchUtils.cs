using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {
    public static class PatchUtils {
        public const BindingFlags BindAllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        public const BindingFlags BindAllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public const BindingFlags BindAll = BindAllStatic | BindAllInstance;

        public static string GetFullName(this FieldInfo field) => $"{field.DeclaringType}.{field.Name}";
        public static string GetFullName(this MethodInfo method) => $"{method.DeclaringType}.{method.Name}";
        public static string GetFullName(this PropertyInfo property) => $"{property.DeclaringType}.{property.Name}";

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
        public static void Virtualize(this MethodInfo method, bool callBase, DisposablePool dpool) {
            if(method.IsStatic) throw new ArgumentException($"Can't virtualize static method {method.GetFullName()}!");
            Type[] parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();
            MethodInfo baseMethod = method.DeclaringType.BaseType.GetMethodRecursive(method.Name, BindAllInstance, parameters);
            dpool.Add(new MethodVirtualizerDetour(baseMethod, method, callBase));
        }

        /// <summary>
        /// Patches the property to bea field proxy. After patching, the property will proxy the given (private) field.
        /// </summary>
        public static void PatchFieldProxy(this PropertyInfo prop, FieldInfo field, DisposablePool dpool) {
            //Check field for compatibility
            if(prop.GetAccessors(true).Any(a => a.IsStatic != field.IsStatic) || prop.PropertyType != field.FieldType) new ArgumentException($"Mismatching field {field.GetFullName()} and property {prop.GetFullName()} types!");
            if(!field.DeclaringType.IsAssignableFrom(prop.DeclaringType)) throw new ArgumentException($"Property {prop.GetFullName()} in different type than field!");

            //Hook getter
            if(prop.GetGetMethod(true) is MethodInfo getter) {
                dpool.Add(new ILHook(getter, ctx => {
                    //Rip out the entire method body
                    ctx.Body.Instructions.Clear();

                    ILCursor cursor = new ILCursor(ctx);

                    //Get the field value
                    if(field.IsStatic) {
                        cursor.Emit(OpCodes.Ldsfld, field);
                    } else {
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.Emit(OpCodes.Ldfld, field);
                    }
                    cursor.Emit(OpCodes.Ret);
                }));
            }

            //Hook setter
            if(prop.GetSetMethod(true) is MethodInfo setter) {
                dpool.Add(new ILHook(setter, ctx => {
                    //Rip out the entire method body
                    ctx.Body.Instructions.Clear();

                    ILCursor cursor = new ILCursor(ctx);

                    //Set the field value
                    if(field.IsStatic) {
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.Emit(OpCodes.Stsfld, field);
                    } else {
                        cursor.Emit(OpCodes.Ldarg_0);
                        cursor.Emit(OpCodes.Ldarg_1);
                        cursor.Emit(OpCodes.Stfld, field);
                    }
                    cursor.Emit(OpCodes.Ret);
                }));
            }
        }

        /// <summary>
        /// Patches a SFX in a vanilla method, by replacing it with one given by a property at runtime. The property can be virtual, but the base implementation must return the SFX to be replaced.
        /// </summary>
        public static void PatchSFX(this MethodInfo method, PropertyInfo sfxProp, DisposablePool dpool) {
            //Check property for compatibility
            MethodInfo sfxPropGetter = sfxProp.GetGetMethod(true);
            if(sfxPropGetter == null || sfxPropGetter.IsStatic || sfxProp.PropertyType != typeof(string)) throw new ArgumentException($"Incompatible SFX property {sfxProp.GetFullName()}!");
            if(!method.DeclaringType.IsAssignableFrom(sfxProp.DeclaringType)) throw new ArgumentException($"SFX property {sfxProp.GetFullName()} in different type than method!");

            //Hook calls to Audio.Play/Loop
            dpool.Add(new ILHook(method, ctx => {
                MethodInfo stringComp = typeof(string).GetMethod(nameof(string.Equals), new Type[] { typeof(string) });

                Dictionary<string, VariableDefinition> argLocals = new Dictionary<string, VariableDefinition>(StringComparer.OrdinalIgnoreCase);

                ILCursor cursor = new ILCursor(ctx);
                bool didPatch = false;
                while(cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(out MethodReference m) && m.DeclaringType.FullName == "Celeste.Audio" && (m.Name == "Play" || m.Name == "Loop"))) {
                    ILLabel restoreArgs = cursor.DefineLabel(), playSfx = cursor.DefineLabel();
                    MethodReference playMethod = (MethodReference) cursor.Instrs[cursor.Index].Operand;

                    //Check if this is a custom type
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Isinst, sfxProp.DeclaringType);
                    cursor.Emit(OpCodes.Brfalse, playSfx);

                    //Save extra arguments
                    for(int i = playMethod.Parameters.Count - 1; i >= 1; i--) {
                        ParameterDefinition param = playMethod.Parameters[i];
                        if(!argLocals.TryGetValue(param.Name, out VariableDefinition var)) {
                            ctx.Body.Variables.Add(var = new VariableDefinition(param.ParameterType));
                            argLocals.Add(param.Name, var);
                        }
                        cursor.Emit(OpCodes.Stloc, var);
                    }

                    //Get the SFX property's *base* value and compare it to the sound effect about to play
                    cursor.Emit(OpCodes.Dup);

                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Castclass, sfxProp.DeclaringType);
                    cursor.Emit(OpCodes.Call, sfxPropGetter);

                    cursor.Emit(OpCodes.Call, stringComp);
                    cursor.Emit(OpCodes.Brfalse, restoreArgs);

                    //Replace the SFX to be played with the SFX property's *actual* value
                    cursor.Emit(OpCodes.Pop);

                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Castclass, sfxProp.DeclaringType);
                    cursor.Emit(OpCodes.Callvirt, sfxPropGetter);

                    //Restore extra arguments
                    cursor.MarkLabel(restoreArgs);
                    for(int i = 1; i < playMethod.Parameters.Count; i++) {
                        cursor.Emit(OpCodes.Ldloc, argLocals[playMethod.Parameters[i].Name]);
                    }

                    cursor.MarkLabel(playSfx);
                    cursor.Index++;

                    didPatch = true;
                }

                if(!didPatch) Logger.Log(LogLevel.Warn, ProcedurlineModule.Name, $"PatchSFX: Found no Audio.Play calls to patch in method {method.GetFullName()}!");
            }));
        }
    }
}