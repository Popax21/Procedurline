using System;
using System.Linq;
using System.Reflection;

using Mono.Cecil.Cil;
using MonoMod.Utils;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.Procedurline {
    //There do be dragons here...
    internal sealed class MethodVirtualizerDetour : IDetour {
        public readonly MethodInfo BaseMethod;
        public readonly MethodInfo VirtualMethod;
        public readonly bool CallBase;

        private Detour baseDetour;
        private MethodBase detourTrampoline, redirTrampoline, baseTrampoline;
        private IntPtr detourTrampolinePtr;
        private NativeDetour baseTrampolineDetour, virtualDetour;

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
                Instruction callVirtualTarget = Instruction.Create(OpCodes.Nop);

                //No inlining
                for(int i = 0; i < 32; i++) il.Emit(OpCodes.Nop);

                //Check if the object is the declaring type
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Isinst, virtualMeth.DeclaringType);
                il.Emit(OpCodes.Brtrue, callVirtualTarget);
                il.Emit(OpCodes.Jmp, baseTrampoline);

                //Call the virtual method
                il.Append(callVirtualTarget);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, virtualMeth.DeclaringType);
                for(int i = 0; i < methParams.Length; i++) il.Emit(OpCodes.Ldarg, 1+i);
                il.Emit(OpCodes.Callvirt, virtualMeth);
                il.Emit(OpCodes.Ret);

                redirTrampoline = methDef.Generate();
            }

            //Apply base detour
            baseDetour = new Detour(baseMeth, redirTrampoline);

            //Fix base trampoline
            //*this* should have been the point where I should have stopped this madness ._.
            detourTrampoline = (MethodBase) typeof(Detour).GetField("_ChainedTrampoline", PatchUtils.BindAllInstance).GetValue(baseDetour);
            detourTrampolinePtr = detourTrampoline.Pin().GetNativeStart();
            baseTrampolineDetour = new NativeDetour(baseTrampoline, detourTrampolinePtr);

            if(callBase) {
                //Replace the virtual method with the base detour trampoline to re-enter the detour chain
                virtualDetour = new NativeDetour(virtualMeth, detourTrampolinePtr);
            }
        }

        public void Dispose() {
            baseDetour?.Dispose();
            baseTrampolineDetour?.Dispose();
            virtualDetour?.Dispose();

            detourTrampoline?.Unpin();

            baseDetour = null;
            detourTrampoline = null;
            detourTrampolinePtr = IntPtr.Zero;

            redirTrampoline = null;
            baseTrampoline = null;
            baseTrampolineDetour = null;

            virtualDetour = null;
        }

        public void Apply() {
            baseDetour?.Apply();
            virtualDetour?.Apply();
        }

        public void Undo() {
            baseDetour?.Undo();
            virtualDetour?.Undo();
        }

        public void Free() {
            baseDetour?.Free();
            virtualDetour?.Free();
        }

        public MethodBase GenerateTrampoline(MethodBase signature = null) => baseDetour?.GenerateTrampoline(signature);
        T IDetour.GenerateTrampoline<T>() => baseDetour.GenerateTrampoline<T>();

        public bool IsValid => baseDetour?.IsValid ?? false;
        public bool IsApplied => baseDetour?.IsApplied ?? false;
    }
}