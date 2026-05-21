namespace AngryMouse.Cursors
{
    internal sealed class CursorRoleRenderSettings
    {
        public CursorRoleRenderSettings()
            : this(0, 0, true)
        {
        }

        public CursorRoleRenderSettings(double hotspotOffsetX, double hotspotOffsetY, bool trimTransparentPadding)
        {
            HotspotOffsetX = hotspotOffsetX;
            HotspotOffsetY = hotspotOffsetY;
            TrimTransparentPadding = trimTransparentPadding;
        }

        public double HotspotOffsetX { get; set; }

        public double HotspotOffsetY { get; set; }

        public bool TrimTransparentPadding { get; set; }

        public CursorRoleRenderSettings Clone()
        {
            return new CursorRoleRenderSettings(HotspotOffsetX, HotspotOffsetY, TrimTransparentPadding);
        }
    }
}
