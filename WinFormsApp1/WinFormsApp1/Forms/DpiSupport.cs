namespace PowerSaver.Forms;

/// <summary>
/// 高 DPI 支持。
/// 本程序的界面全部用代码按 96 DPI 的像素坐标搭建；若不启用自动缩放，
/// 字体在高缩放下会随 DPI 变大，而控件尺寸保持不变，200% 时就会出现元素重叠。
/// 这里统一让窗体按当前监视器的 DPI 比例整体缩放布局。
/// </summary>
internal static class DpiSupport
{
    /// <summary>在创建/摆放控件之前调用。</summary>
    public static void Enable(Form form)
    {
        // 以 96 DPI 作为设计基准；运行时会按 DeviceDpi / 96 的比例缩放整个布局。
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
    }
}
