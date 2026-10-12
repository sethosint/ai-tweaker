using AITweaker.Core.Model;

namespace AITweaker.App.Platform.Actions;

public static class DisplayOps
{
    public static Task ApplyAsync(DisplayRefreshAction a, ExecContext ctx)
    {
        foreach (var d in Native.GetDisplays().Where(d => d.MaxRefreshHz > d.RefreshHz))
        {
            if (Native.SetRefreshRate(d.Device, d.Width, d.Height, d.MaxRefreshHz))
            {
                ctx.Record(ChangeRecord.Of("display", ("device", d.Device), ("width", d.Width.ToString()), ("height", d.Height.ToString()), ("hz", d.RefreshHz.ToString())));
                ctx.Log($"{d.Name}: {d.RefreshHz} Hz -> {d.MaxRefreshHz} Hz");
            }
            else
            {
                ctx.Log($"{d.Name}: driver refused {d.MaxRefreshHz} Hz");
            }
        }
        return Task.CompletedTask;
    }

    public static Task RevertAsync(ChangeRecord r)
    {
        Native.SetRefreshRate(r["device"]!, int.Parse(r["width"]!), int.Parse(r["height"]!), int.Parse(r["hz"]!));
        return Task.CompletedTask;
    }

    public static bool? Check() => Native.GetDisplays().All(d => d.RefreshHz >= d.MaxRefreshHz);
}
