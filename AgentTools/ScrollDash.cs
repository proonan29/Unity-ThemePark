using UnityEngine;
using UnityEngine.UIElements;

// Play mode: scroll the control-room dashboard (for screenshots).
// Run: tp.ps1 run_script --file AgentTools/ScrollDash.cs --entry ScrollDash.Mid --result-only
public static class ScrollDash
{
    static string To(float y)
    {
        var doc = Object.FindAnyObjectByType<UIDocument>();
        if (doc == null) return "no UIDocument";
        var sv = doc.rootVisualElement.Q<ScrollView>("DashboardScroll");
        if (sv == null) return "no dashboard scroll view";
        sv.scrollOffset = new Vector2(0f, y);
        return $"scrolled to {y} (content {sv.contentContainer.layout.height})";
    }
    public static string Top() => To(0f);
    public static string Mid() => To(820f);
    public static string Bottom() => To(5000f);
}
