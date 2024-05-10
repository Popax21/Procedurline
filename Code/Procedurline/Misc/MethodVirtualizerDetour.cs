using System;
using System.Linq;
using System.Reflection;

using Mono.Cecil.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;
using Celeste.Mod.Helpers.LegacyMonoMod;
using MonoMod.Core;
using MonoMod.Core.Platforms;

namespace Celeste.Mod.Procedurline {
    //There do be dragons here...
    /// <summary>
    /// Implements a detour which redirects invocations of a non-virtual base method to a virtual method contained in a child class, if the instance is of that type.
    /// Optionally also redirects the virtual method's base implementation back to the non-virtual one, which allows one to retroactively make a method virtual.
    /// </summary>
    public sealed class MethodVirtualizerDetour : IDisposable {
        public readonly MethodInfo BaseMethod;
        public readonly MethodInfo VirtualMethod;
        public readonly bool CallBase;

        private MethodInfo baseTrampoline, redirTrampoline;
        private Hook baseHooke;
        private ICoreDetour baseTrampolineDetour, virtualMethDetour;

        public MethodVirtualizerDetour(MethodInfo baseMeth, MethodInfo virtualMeth, bool callBase) {
            if(baseMeth.IsStatic || virtualMeth.IsStatic) throw new ArgumentException("Can't virtualize static methods!");
            if(!baseMeth.DeclaringType.IsAssignableFrom(virtualMeth.DeclaringType)) throw new ArgumentException("Virtual method not in subtype of base method!");

            BaseMethod = baseMeth;
            VirtualMethod = virtualMeth;
            CallBase = callBase;

            Type[] methParams = baseMeth.GetParameters().Select(p => p.ParameterType).ToArray();
            Type[] methThisParams = Enumerable.Repeat(baseMeth.GetThisParamType(), 1).Concat(methParams).ToArray();

            if(!virtualMeth.GetParameters().Select(p => p.ParameterType).SequenceEqual(methParams) || baseMeth.ReturnParameter.ParameterType != virtualMeth.ReturnParameter.ParameterType) throw new ArgumentException("Argument type mistmatch between base and virtual method!");

            //Create the "base trampoline trampoline"(tm)
            using(DynamicMethodDefinition methDef = new DynamicMethodDefinition($"PL_BaseTramp<{baseMeth.GetID(simple: true)}>?{GetHashCode()}", baseMeth.ReturnType, methThisParams)) {
                baseTrampoline = methDef.StubCriticalDetour().Generate();
            }

            //Create the redirection trampoline
            using(DynamicMethodDefinition methDef = new DynamicMethodDefinition($"PL_VirtualRedir<{baseMeth.GetID(simple: true)}>?{GetHashCode()}", baseMeth.ReturnType, methThisParams)) {
                ILProcessor il = methDef.GetILProcessor();
                methDef.Definition.NoInlining = true;

                //Check if the object is the declaring type
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Isinst, virtualMeth.DeclaringType);
                int brIdx = il.Body.Instructions.Count;
                il.Emit(OpCodes.Brfalse, il.Body.Instructions[0]);

                //Call the virtual method
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, virtualMeth.DeclaringType);
                for(int i = 0; i < methParams.Length; i++) il.Emit(OpCodes.Ldarg, 1+i);
                il.Emit(OpCodes.Callvirt, virtualMeth);
                il.Emit(OpCodes.Ret);

                il.Emit(OpCodes.Jmp, baseTrampoline);
                il.Body.Instructions[brIdx].Operand = il.Body.Instructions[il.Body.Instructions.Count-1];

                redirTrampoline = methDef.Generate();
            }

            //Apply base hook
            baseHooke = new Hook(baseMeth, redirTrampoline);

            //Fix base trampoline
            object nextTrampoline = typeof(Hook).GetInterfaces().First(interf => interf.Name == "IDetour").GetProperty("NextTrampoline", PatchUtils.BindAllInstance).GetValue(baseHooke);
            MethodInfo trampolineMethod = (MethodInfo) nextTrampoline.GetType().GetProperty("TrampolineMethod", PatchUtils.BindAllInstance).GetValue(nextTrampoline);
            baseTrampolineDetour = DetourFactory.Current.CreateDetour(baseTrampoline, trampolineMethod);

            if(callBase) {
                //Detour the virtual method to the base trampoline
                virtualMethDetour = DetourFactory.Current.CreateDetour(virtualMeth, trampolineMethod);
            }
        }

        public void Dispose() {
            baseHooke?.Dispose();
            baseTrampolineDetour?.Dispose();

            baseHooke = null;

            redirTrampoline = null;
            baseTrampoline = null;
            baseTrampolineDetour = null;

            virtualMethDetour?.Dispose();
            virtualMethDetour = null;
        }

        public void Apply() {
            baseHooke?.Apply();
            virtualMethDetour?.Apply();
        }

        public void Undo() {
            baseHooke?.Undo();
            virtualMethDetour?.Undo();
        }

        public bool IsValid => baseHooke?.IsValid ?? false;
        public bool IsApplied => baseHooke?.IsApplied ?? false;
    }
}