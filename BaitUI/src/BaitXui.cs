using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;

// Dedicated servers send their already-patched XUi to every client. Bait UI is client-only, so those
// files never include the roster windows. Re-apply our XML on this machine before XUi parses them,
// and register the groups on a live HUD if they were still missing when the player hits Home.
public static class BaitXui
{
    public static Mod Mod;
    static readonly string[] Groups = { "azraelBaitRoster", "azraelBaitRoster12", "azraelBaitRoster16", "azraelBaitStats", "azraelBaitName", "azraelBaitAdmin", "azraelBaitSetup", "azraelBaitBestiary", "azraelBaitSummary" };

    public static void ApplyIfNeeded(XmlFile xml)
    {
        if (xml == null || xml.XmlDoc == null || xml.XmlDoc.Root == null || Mod == null) return;
        try
        {
            string root = xml.XmlDoc.Root.Name.LocalName;
            if (root == "windows")
                Apply(xml, Path.Combine("Config", "XUi_InGame", "windows.xml"), "windowAzraelBaitRoster");
            else if (root == "xui")
                Apply(xml, Path.Combine("Config", "XUi_InGame", "xui.xml"), "azraelBaitRoster");
        }
        catch (Exception e) { Log.Warning("[BaitUI] XUi patch: " + e.Message); }
    }

    public static bool TryRegister(LocalPlayerUI ui)
    {
        if (ui == null || ui.xui == null || Mod == null) return false;
        try
        {
            if (ui.xui.GetWindowGroupById("azraelBaitRoster") != null) return true;
            EnsureWindowDefs();
            EnsureGroupsOnRoot();
            foreach (string name in Groups)
            {
                if (ui.xui.GetWindowGroupById(name) != null) continue;
                XUiFromXml.LoadXui(ui.xui, name);
                var g = ui.xui.GetWindowGroupById(name);
                if (g != null && !g.Initialized) g.Init();
            }
            return ui.xui.GetWindowGroupById("azraelBaitRoster") != null;
        }
        catch (Exception e) { Log.Warning("[BaitUI] register roster: " + e.Message); return false; }
    }

    static void Apply(XmlFile target, string rel, string marker)
    {
        if (HasName(target.XmlDoc.Root, marker)) return;
        string path = Path.Combine(Mod.Path, rel);
        if (!File.Exists(path)) { Log.Warning("[BaitUI] missing " + path); return; }
        var patch = XmlPatcher.ReadPatchXmlWithFixedModFolders(Mod, path);
        if (patch == null || patch.XmlDoc == null || patch.XmlDoc.Root == null)
        {
            Log.Warning("[BaitUI] could not read " + rel);
            return;
        }
        if (!XmlPatcher.PatchXml(target, patch.XmlDoc.Root, patch, Mod))
            Log.Warning("[BaitUI] XUi patch did not apply: " + rel);
        else
            Log.Out("[BaitUI] applied client XUi onto " + target.Filename);
    }

    static void EnsureWindowDefs()
    {
        if (WindowDataHas("windowAzraelBaitRoster")) return;
        string path = Path.Combine(Mod.Path, "Config", "XUi_InGame", "windows.xml");
        var patch = XmlPatcher.ReadPatchXmlWithFixedModFolders(Mod, path);
        if (patch == null || patch.XmlDoc == null || patch.XmlDoc.Root == null) return;
        var wrap = new XElement("windows");
        foreach (var append in patch.XmlDoc.Root.Elements("append"))
            foreach (var w in append.Elements())
                wrap.Add(new XElement(w));
        Drain(XUiFromXml.Load(new XmlFile(wrap.ToString(), "", "windows", true)));
    }

    static void EnsureGroupsOnRoot()
    {
        var root = MainXuiRoot();
        if (root == null || HasName(root, "azraelBaitRoster")) return;
        string path = Path.Combine(Mod.Path, "Config", "XUi_InGame", "xui.xml");
        var patch = XmlPatcher.ReadPatchXmlWithFixedModFolders(Mod, path);
        if (patch == null || patch.XmlDoc == null || patch.XmlDoc.Root == null) return;
        foreach (var append in patch.XmlDoc.Root.Elements("append"))
            foreach (var g in append.Elements())
                root.Add(new XElement(g));
    }

    static bool WindowDataHas(string name)
    {
        var f = typeof(XUiFromXml).GetField("windowData", BindingFlags.NonPublic | BindingFlags.Static);
        var d = f != null ? f.GetValue(null) as System.Collections.IDictionary : null;
        return d != null && d.Contains(name);
    }

    static XElement MainXuiRoot()
    {
        var f = typeof(XUiFromXml).GetField("mainXuiXmlRoot", BindingFlags.NonPublic | BindingFlags.Static);
        return f != null ? f.GetValue(null) as XElement : null;
    }

    static bool HasName(XElement root, string name)
    {
        if (root == null) return false;
        foreach (var e in root.DescendantsAndSelf())
        {
            var a = e.Attribute("name");
            if (a != null && a.Value == name) return true;
        }
        return false;
    }

    static void Drain(IEnumerator e)
    {
        if (e == null) return;
        while (e.MoveNext()) { }
    }
}

[HarmonyPatch(typeof(XUiFromXml), nameof(XUiFromXml.Load))]
public static class BaitXuiLoadPatch
{
    static void Prefix(XmlFile _xmlFile)
    {
        BaitXui.ApplyIfNeeded(_xmlFile);
    }
}
