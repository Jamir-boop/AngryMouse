namespace AngryMouse.Cursors
{
    internal sealed class CursorRoleRenderSettings
    {
        private const double MaximumHotspotOffset = CursorVisualLoader.BuiltInCursorHeight;
        private double _hotspotOffsetX;
        private double _hotspotOffsetY;

        public CursorRoleRenderSettings()
            : this(0, 0)
        {
        }

        public CursorRoleRenderSettings(double hotspotOffsetX, double hotspotOffsetY)
        {
            HotspotOffsetX = hotspotOffsetX;
            HotspotOffsetY = hotspotOffsetY;
        }

        public double HotspotOffsetX
        {
            get => _hotspotOffsetX;
            set => _hotspotOffsetX = NormalizeHotspotOffset(value);
        }

        public double HotspotOffsetY
        {
            get => _hotspotOffsetY;
            set => _hotspotOffsetY = NormalizeHotspotOffset(value);
        }

        public CursorRoleRenderSettings Clone()
        {
            return new CursorRoleRenderSettings(HotspotOffsetX, HotspotOffsetY);
        }

        private static double NormalizeHotspotOffset(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            return System.Math.Max(-MaximumHotspotOffset, System.Math.Min(MaximumHotspotOffset, value));
        }
    }
}
