using UnityEngine;
using ThemePark;

// After reopening the saved scene: are all ThemePark components still attached (no missing scripts)?
public static class CheckScene
{
    public static string Run()
    {
        int missing = 0;
        foreach (var go in Object.FindObjectsByType<GameObject>())
            foreach (var c in go.GetComponents<Component>()) if (c == null) missing++;
        return $"SimRunner={Object.FindObjectsByType<SimRunner>().Length} VisitorRenderer={Object.FindObjectsByType<VisitorRenderer>().Length} " +
               $"OverlayRenderer={Object.FindObjectsByType<OverlayRenderer>().Length} RouteView={Object.FindObjectsByType<RouteView>().Length} " +
               $"RideAnimator={Object.FindObjectsByType<RideAnimator>().Length} CameraRig={Object.FindObjectsByType<CameraRig>().Length} " +
               $"AppController={Object.FindObjectsByType<AppController>().Length} UI={Object.FindObjectsByType<ThemePark.UI.ThemeParkUI>().Length} missingScripts={missing}";
    }
}
