using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Mono.Cecil.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {
    //There do be dragons here...
    internal sealed class MethodVirtualizerDetour : IDetour {
        public readonly MethodInfo BaseMethod;
        public readonly MethodInfo VirtualMethod;
        public readonly bool CallBase;

        private MethodBase detourTrampoline, redirTrampoline, baseTrampoline;
        private Detour baseDetour;
        private NativeDetour baseTrampolineDetour, virtualMethDetour;

        public MethodVirtualizerDetour(MethodInfo baseMeth, MethodInfo virtualMeth, bool callBase) {
            BaseMethod = baseMeth;
            VirtualMethod = virtualMeth;
            CallBase = callBase;

            Type[] methParams = baseMeth.GetParameters().Select(p => p.ParameterType).ToArray();
            Type[] methThisParams = methParams.Prepend(baseMeth.GetThisParamType()).ToArray();

            //Create "base trampoline trampoline"(tm)
            using(DynamicMethodDefinition methDef = new DynamicMethodDefinition($"PL_BaseTramp<{baseMeth.GetID(simple: true)}>?{GetHashCode()}", baseMeth.ReturnType, methThisParams)) {
                baseTrampoline = methDef.StubCriticalDetour().Generate();
            }

            //Create redirection trampoline
            using(DynamicMethodDefinition methDef = new DynamicMethodDefinition($"PL_VirtualRedir<{baseMeth.GetID(simple: true)}>?{GetHashCode()}", baseMeth.ReturnType, methThisParams)) {
                ILProcessor il = methDef.GetILProcessor();

                //No inlining
                for(int i = 0; i < 32; i++) il.Emit(OpCodes.Nop);

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

            //Apply base detour
            baseDetour = new Detour(baseMeth, redirTrampoline);

            //Fix base trampoline
            detourTrampoline = (MethodBase) typeof(Detour).GetField("_ChainedTrampoline", PatchUtils.BindAllInstance).GetValue(baseDetour);
            baseTrampolineDetour = new NativeDetour(baseTrampoline, detourTrampoline);

            if(callBase) {
                //Detour the virtual method to the base trampoline
                virtualMethDetour = new NativeDetour(virtualMeth, detourTrampoline);
            }
        }

        public void Dispose() {
            baseDetour?.Dispose();
            baseTrampolineDetour?.Dispose();

            baseDetour = null;
            detourTrampoline = null;

            redirTrampoline = null;
            baseTrampoline = null;
            baseTrampolineDetour = null;

            virtualMethDetour?.Dispose();
            virtualMethDetour = null;
        }

        public void Apply() {
            baseDetour?.Apply();
            virtualMethDetour?.Apply();
        }

        public void Undo() {
            baseDetour?.Undo();
            virtualMethDetour?.Undo();
        }

        public void Free() {
            baseDetour?.Free();
            virtualMethDetour?.Free();
        }

        public MethodBase GenerateTrampoline(MethodBase signature = null) => baseDetour?.GenerateTrampoline(signature);
        T IDetour.GenerateTrampoline<T>() => baseDetour?.GenerateTrampoline<T>();

        public bool IsValid => baseDetour?.IsValid ?? false;
        public bool IsApplied => baseDetour?.IsApplied ?? false;
    }
}