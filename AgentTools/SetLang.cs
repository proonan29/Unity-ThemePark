// Play mode: switch the UI language.
public static class SetLangEn { public static string Run() { ThemePark.UI.Loc.Korean = false; return "lang=en"; } }
public static class SetLangKo { public static string Run() { ThemePark.UI.Loc.Korean = true; return "lang=ko"; } }
