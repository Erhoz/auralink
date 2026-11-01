namespace AuraLink.Core
{
    public struct RgbColor
    {
        public readonly byte Red, Green, Blue;
        public RgbColor(byte red, byte green, byte blue) { Red=red; Green=green; Blue=blue; }
        public override string ToString() { return Red.ToString("X2")+Green.ToString("X2")+Blue.ToString("X2"); }
    }
}
