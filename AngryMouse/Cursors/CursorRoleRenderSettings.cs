namespace AngryMouse.Cursors
{
    internal sealed class CursorRoleRenderSettings
    {
        public CursorRoleRenderSettings()
            : this(0, 0)
        {
        }

        public CursorRoleRenderSettings(double hotspotOffsetX, double hotspotOffsetY)
        {
            HotspotOffsetX = hotspotOffsetX;
            HotspotOffsetY = hotspotOffsetY;
        }

        public double HotspotOffsetX { get; set; }

        public double HotspotOffsetY { get; set; }

        public CursorRoleRenderSettings Clone()
        {
            return new CursorRoleRenderSettings(HotspotOffsetX, HotspotOffsetY);
        }
    }
}
