#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public sealed class ScriptableObjectCreatorWindow : EditorWindow
{
    private const string MenuName = "Assets/Create/ScriptableObject (using Creator)";
    private const string SearchTextControlName = nameof(ScriptableObjectCreatorWindow) + ".Search";
    private static readonly char[] SearchTextSplitSeparator = { ' ' };
    private static readonly GUILayoutOption[] ClearButtonGUIOptions = { GUILayout.ExpandWidth(false) };

    private static readonly string[] ExcludeAssemblyPrefixes =
    {
        "Unity.",
        "UnityEngine",
        "UnityEditor",
    };

    private static readonly Type[] ExcludeSubclasses = new Type[]
    {
        typeof(Editor),
        typeof(EditorWindow),
        Type.GetType("UnityEngine.StateMachineBehaviour,UnityEngine.AnimationModule"),
        Type.GetType("UnityEngine.Timeline.Marker,Unity.Timeline"),
    }.Where(t => t != null).ToArray();

    private class ListItem
    {
        public readonly Type Type;
        public readonly string FullName;
        public readonly string CreateMenuName;
        public readonly string CreateFileName;
        public string DisplayName => CreateMenuName ?? FullName;

        public ListItem(Type type)
        {
            Type = type;
            FullName = type.FullName ?? string.Empty;
            CreateFileName = type.Name + ".asset";
            var createAssetMenuAttribute = type.GetCustomAttribute<CreateAssetMenuAttribute>();
            if (createAssetMenuAttribute != null)
            {
                if (!string.IsNullOrEmpty(createAssetMenuAttribute.menuName))
                    CreateMenuName = createAssetMenuAttribute.menuName;
                if (!string.IsNullOrEmpty(createAssetMenuAttribute.fileName))
                    CreateFileName = createAssetMenuAttribute.fileName.Replace('/', '_');
            }
        }
    }

    private static ListItem[] s_allItems;

    private Vector2 _scroll;
    private string _searchText;
    private string _destinationDirectory;
    private List<ListItem> _matchItems = new List<ListItem>();
    private string _focusTo;

    [MenuItem(MenuName)]
    private static void Launch()
    {
        var selectedAsset = GetSelectedAsset();
        var destinationDirectory = GetAssetDirectoryPath(selectedAsset);
        if (selectedAsset is MonoScript monoScript)
        {
            CreateScriptableObject(new ListItem(monoScript.GetClass()), destinationDirectory);
            return;
        }

        var window = CreateInstance<ScriptableObjectCreatorWindow>();
        window.titleContent = new GUIContent("ScriptableObjectCreator");
        window._destinationDirectory = destinationDirectory;
        window.ShowModal();
    }

    private void OnEnable()
    {
        if (s_allItems == null)
        {
            s_allItems = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(IsTargetScriptableObject)
                .Select(t => new ListItem(t))
                .OrderBy(type => type.DisplayName)
                .ToArray();
        }

        ApplySearch();
        _focusTo = SearchTextControlName;
    }

    private void OnGUI()
    {
        string nextFocusControlName = null;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.SetNextControlName(SearchTextControlName);
            var text = EditorGUILayout.TextField(_searchText);
            if (text != _searchText)
            {
                _searchText = text;
                ApplySearch();
            }

            if (GUILayout.Button("clear", ClearButtonGUIOptions))
            {
                _searchText = null;

                GUI.FocusControl("");
                nextFocusControlName = SearchTextControlName;

                ApplySearch();
            }
        }

        using (var scrollScope = new EditorGUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scrollScope.scrollPosition;

            if (_matchItems.Count > 0)
            {
                foreach (var item in _matchItems)
                {
                    if (GUILayout.Button(item.DisplayName))
                    {
                        Close();
                        // NOTE: Unity2019で名前入力が正常に動作しなかったのでdelayCallで実行するようにしています
                        EditorApplication.delayCall += () => CreateScriptableObject(item, _destinationDirectory);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Not found.");
            }
        }

        if (!string.IsNullOrEmpty(_focusTo))
        {
            GUI.FocusControl(_focusTo);
            _focusTo = null;
        }

        if (!string.IsNullOrEmpty(nextFocusControlName))
        {
            _focusTo = nextFocusControlName;
            Repaint();
        }
    }

    private void ApplySearch()
    {
        _matchItems.Clear();

        var tokens = ParseTokens(_searchText);
        if (tokens.Length == 0)
        {
            _matchItems.AddRange(s_allItems);
            return;
        }

        foreach (var item in s_allItems)
        {
            if (IsMatch(item, tokens))
            {
                _matchItems.Add(item);
            }
        }
    }

    private static string[] ParseTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        return text.Split(SearchTextSplitSeparator)
            .Select(s => s.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
    }

    private static bool IsMatch(ListItem item, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (item.FullName.IndexOf(token, StringComparison.OrdinalIgnoreCase) != -1)
                continue;

            if (item.CreateMenuName != null && item.CreateMenuName.IndexOf(token, StringComparison.OrdinalIgnoreCase) != -1)
                continue;

            return false;
        }
        return true;
    }

    private static void CreateScriptableObject(ListItem listItem, string destinationDirectory)
    {
        if (string.IsNullOrEmpty(destinationDirectory))
            destinationDirectory = "Assets";

        var asset = CreateInstance(listItem.Type);
        var assetPath = string.Format("{0}/{1}", destinationDirectory, listItem.CreateFileName);
        ProjectWindowUtil.CreateAsset(asset, assetPath);
    }

    private static UnityEngine.Object GetSelectedAsset()
    {
        return Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets).FirstOrDefault();
    }

    private static string GetAssetDirectoryPath(UnityEngine.Object asset)
    {
        if (asset == null)
            return null;

        var assetPath = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(assetPath))
            return null;

        if (AssetDatabase.IsValidFolder(assetPath))
            return assetPath;

        var lastSeparatorIndex = assetPath.LastIndexOf('/');
        if (lastSeparatorIndex != -1)
        {
            var path = assetPath.Substring(0, lastSeparatorIndex);
            if (AssetDatabase.IsValidFolder(path))
                return path;
        }
        return null;
    }

    private static bool IsTargetScriptableObject(Type type)
    {
        if (!type.IsPublic && !type.IsNestedPublic)
            return false;

        if (type.IsAbstract || type.IsGenericType)
            return false;

        var assemblyFullname = type.Assembly.FullName;
        foreach (var excludeAssemblyPrefix in ExcludeAssemblyPrefixes)
        {
            if (assemblyFullname.StartsWith(excludeAssemblyPrefix, StringComparison.Ordinal))
                return false;
        }

        foreach (var excludeSubclass in ExcludeSubclasses)
        {
            if (type.IsSubclassOf(excludeSubclass))
                return false;
        }
        return true;
    }
}
#endif
