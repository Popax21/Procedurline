namespace Celeste.Mod.Procedurline {
    /// <summary>
    /// An empty struct used to NOP out generic type arguments.
    /// </summary>
    public struct VoidBox {
        public override bool Equals(object obj) => false;
        public override int GetHashCode() => unchecked((int) 0xdeadc0de);
    }
}