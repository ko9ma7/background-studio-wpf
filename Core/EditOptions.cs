namespace BackgroundStudio.Core;

public sealed record EditOptions(
    BackgroundMode Mode,
    string Color,
    double BlurRadius,
    double ShadowBlur,
    double ShadowOpacity,
    double ShadowOffsetX,
    double ShadowOffsetY,
    double MaskThreshold,
    double EdgeSoftness);
