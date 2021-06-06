#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace MCU.Registry {
    public static partial class MCURegistry {
        /// <summary>
        /// Contain a collection of functions and values that are only usable with the registry within the Unity Editor
        /// </summary>
        [InitializeOnLoad]
        public static class Editor {
            /*----------Types----------*/
            //PRIVATE

            /// <summary>
            /// A basic breakdown of a package file that has been identified as included in the project
            /// </summary>
            [Serializable]
            private struct PackageFile {
                /// <summary>
                /// The name that is stored within this package file
                /// </summary>
                public string name;

                /// <summary>
                /// The raw json data that can be used to load the additional data on identification
                /// </summary>
                [NonSerialized]
                public string rawJson;
            }

            //PUBLIC

            /// <summary>
            /// Store information about a package that has been identified within the project
            /// </summary>
            [Serializable]
            public sealed class PackageData : ISerializationCallbackReceiver {
                /*----------Data----------*/
                //PUBLIC

                ////////////////////////////////////////////////////////////////////////////////////////////////////
                //////////------------------------------Package Information-------------------------------//////////
                ////////////////////////////////////////////////////////////////////////////////////////////////////

                /// <summary>
                /// The name that has been assigned to the package
                /// </summary>
                public string Name { get { return name; } }
                [SerializeField] private string name = null;

                /// <summary>
                /// The display name that is shown when viewing this package
                /// </summary>
                public string DisplayName { get { return displayName; } }
                [SerializeField] private string displayName = null;

                /// <summary>
                /// The current version of this package 
                /// </summary>
                public string Version { get { return version; } }
                [SerializeField] private string version = null;

                /// <summary>
                /// The description that has been assigned to this package
                /// </summary>
                public string Description { get { return description; } }
                [SerializeField] private string description = null;

                /// <summary>
                /// The keywords that have been assigned to this package
                /// </summary>
                public HashSet<string> Keywords { get; private set; } = new HashSet<string>();
                [SerializeField] private string[] keywords = null;

                /// <summary>
                /// The labels that will be assigned to assets related to this package
                /// </summary>
                public HashSet<string> Labels { get; private set; } = new HashSet<string>();
                [SerializeField] private string[] labels = null;

                /// <summary>
                /// The raw JSON data that contains the extraced package data
                /// </summary>
                public string RawPackageJson { get; private set; }

                ////////////////////////////////////////////////////////////////////////////////////////////////////
                //////////------------------------------Assembly Information------------------------------//////////
                ////////////////////////////////////////////////////////////////////////////////////////////////////

                /// <summary>
                /// The Assembly Asset in the project that corresponds to this package
                /// </summary>
                public AssemblyDefinitionAsset AssemblyAsset { get; private set; }

                /// <summary>
                /// The loaded assembly object that is associated with this package
                /// </summary>
                public Assembly PackageAssembly { get; private set; }

                /*----------Functions----------*/
                //PUBLIC

                /// <summary>
                /// Create of this data container with the specified values
                /// </summary>
                /// <param name="asset">The project asset that corresponds with this package description</param>
                /// <param name="assembly">The assembly object that is associated with the assembly definition</param>
                /// <param name="packageJson">The raw JSON string that describes the package that was identifed</param>
                public PackageData(AssemblyDefinitionAsset asset, Assembly assembly, string packageJson) {
                    // If there is JSON data, load that over the top of this object first
                    if (!string.IsNullOrEmpty(packageJson)) {
                        // Load the packaged JSON data into this objects memory
                        try { JsonUtility.FromJsonOverwrite(packageJson, this); }

                        // If anything goes wrong, then we can't load the package description data
                        catch (Exception exec) {
                            Debug.LogError($"Unable to parse the package description JSON associated with {asset}. ERROR: {exec}", asset);
                            packageJson = null;
                        }
                    }

                    // If there are no names set for this package, take it from the asset
                    if (string.IsNullOrEmpty(name)) name = asset.name;
                    if (string.IsNullOrEmpty(displayName)) displayName = ObjectNames.NicifyVariableName(asset.name.Replace(".", string.Empty));

                    // Stash the parameter data
                    AssemblyAsset = asset;
                    PackageAssembly = assembly;
                    RawPackageJson = packageJson;
                }

                /// <summary>
                /// Ignored
                /// </summary>
                public void OnBeforeSerialize() {}

                /// <summary>
                /// Copy the required data data to the lookup containers
                /// </summary>
                public void OnAfterDeserialize() {
                    Keywords.Clear();
                    Labels.Clear();
                    if (keywords != null) Keywords.UnionWith(keywords);
                    if (labels != null) Labels.UnionWith(labels);
                }

                /// <summary>
                /// Use the display name and version as the string representation of this object
                /// </summary>
                /// <returns>Returns a combined string with the basics of the package information</returns>
                public override string ToString() { return $"{displayName} ({version})"; }
            }

            /*----------Variables----------*/
            //CONST

            /// <summary>
            /// The different characters that can be used to split out directories that will be processed
            /// </summary>
            private static readonly char[] DIRECTORY_SEPARATORS = new char[] { '/', '\\' };

            /// <summary>
            /// Store the current working directory for processing relative elements
            /// </summary>
            public static readonly string WORKING_DIRECTORY = Directory.GetCurrentDirectory().Replace('\\', '/') + "/";

            //PRIVATE

            /// <summary>
            /// Store the different packages that are identified in the project
            /// </summary>
            private static List<PackageData> includedPackages = new List<PackageData>();

            /*----------Properties----------*/
            //PUBLIC

            /// <summary>
            /// The collection of packages that have been identified within the current project
            /// </summary>
            public static IReadOnlyList<PackageData> IncludedPackages { get { return includedPackages; } }

            /*----------Functions----------*/
            //PRIVATE

            /// <summary>
            /// Identify the packages that are included in the current project
            /// </summary>
            static Editor() {
                IdentifyProjectPackages();
                ApplyPackageLabels();
                ApplyAdapterDefines();
            }

            /// <summary>
            /// Find all of the usable package descriptions that currently exist within the project
            /// </summary>
            private static void IdentifyProjectPackages() {
                // Build a lookup collection of the different assemblies within the project
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Dictionary<string, Assembly> assemblyLookup = new Dictionary<string, Assembly>(assemblies.Length);
                for (int i = 0; i < assemblies.Length; ++i)
                    assemblyLookup[assemblies[i].GetName().Name.ToLowerInvariant()] = assemblies[i];

                // Get all of the package descriptions that are included in this project
                DirectoryInfo workingDir = new DirectoryInfo(WORKING_DIRECTORY);
                FileInfo[] packageFileInfos = workingDir.GetFiles("package.json", SearchOption.AllDirectories);
                PackageFile[] packageFiles = new PackageFile[packageFileInfos.Length];
                for (int i = 0; i < packageFileInfos.Length; ++i) {
                    // Try to deserialise the package data into the container
                    try {
                        // Try to load the JSON data from the file
                        string json = File.ReadAllText(packageFileInfos[i].FullName);

                        // Try to deserialise the JSON data into the current container
                        packageFiles[i] = JsonUtility.FromJson<PackageFile>(json);

                        // Store the values that are needed for processing
                        packageFiles[i].name = (string.IsNullOrEmpty(packageFiles[i].name) ?
                            string.Empty :
                            packageFiles[i].name.ToLowerInvariant()
                        );
                        packageFiles[i].rawJson = json;
                    }

                    // If anything goes wrong, assume this is a bust
                    catch (Exception exec) {
                        packageFiles[i].name =
                        packageFiles[i].rawJson = string.Empty;
                        Debug.LogWarning($"Unable to parse '{packageFileInfos[i].FullName.Substring(WORKING_DIRECTORY.Length)}'. ERROR: {exec}");
                    }
                }

                // Look for Assembly Definition Assets in the project that need to be processed
                string[] assetIds = AssetDatabase.FindAssets($"t:{nameof(AssemblyDefinitionAsset)}");
                for (int i = 0; i < assetIds.Length; ++i) {
                    // Get the path to the asset that is to be loaded
                    string path = AssetDatabase.GUIDToAssetPath(assetIds[i]);

                    // Try to load the assembly definition asset at this location
                    AssemblyDefinitionAsset asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
                    if (asset) {
                        // Identify the package JSON that will be used
                        string packageJson = null;
                        string lowerName = asset.name.ToLowerInvariant();
                        for (int ii = 0; ii < packageFiles.Length; ++ii) {
                            // Check if this is the associated package data
                            if (!string.IsNullOrEmpty(packageFiles[ii].name) && packageFiles[ii].name.Contains(lowerName)) {
                                packageJson = packageFiles[ii].rawJson;
                                break;
                            }
                        }

                        // Look for the assembly that matches the name of this package
                        Assembly packageAssembly = null;
                        foreach (var pair in assemblyLookup) {
                            // Check if this assembly has a mathching name
                            if (pair.Key.Contains(lowerName)) {
                                packageAssembly = pair.Value;
                                break;
                            }
                        }

                        // Add the package description to the collection
                        includedPackages.Add(new PackageData(
                            asset,
                            packageAssembly,
                            packageJson
                        ));
                    }
                }
            }

            //PUBLIC 

            ////////////////////////////////////////////////////////////////////////////////////////////////////
            //////////----------------------------Directory Identification----------------------------//////////
            ////////////////////////////////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Find the specified search directory within the current project assets
            /// </summary>
            /// <param name="searchDirectory">The directory that is being looked for</param>
            /// <returns>Returns the first matching directory path found or null if not found</returns>
            /// <remarks>
            /// A valid path that is returned by this search will start with "Assets/..."
            /// </remarks>
            public static string FindAssetDirectory(string searchDirectory) {
                // If there is no directory, then we can't search for anything
                if (string.IsNullOrEmpty(searchDirectory)) return null;

                // Divide the specified search string into the different segments
                string[] searchStages = searchDirectory.Split(DIRECTORY_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);

                // If there are no stages then don't bother
                if (searchStages.Length == 0) return null;

                // Look for the chain of directories that are needed to identify the correct location
                Queue<DirectoryInfo> unsearched = new Queue<DirectoryInfo>();
                unsearched.Enqueue(new DirectoryInfo(WORKING_DIRECTORY + "Assets/"));

                // Process the contained directories until a match is found
                DirectoryInfo foundDirectory = null;
                while (unsearched.Count > 0) {
                    // Get the next directory to be processed
                    DirectoryInfo current = unsearched.Dequeue();

                    // If this directory is the start of the process, look for the sub sections
                    if (current.Name == searchStages[0]) {
                        // If this is the only level of the search then we're good
                        if (searchStages.Length == 1)
                            foundDirectory = current;

                        // Otherwise, look for the child directories that are needed
                        else {
                            // Store the parent directory that is currently being searched
                            DirectoryInfo parent = current;

                            // Look for each of the child stages that are needed
                            for (int i = 1; i < searchStages.Length; ++i) {
                                // Try to find the directory at this stage
                                DirectoryInfo[] stages = parent.GetDirectories(searchStages[i], SearchOption.TopDirectoryOnly);
                                if (stages.Length == 0) break;

                                // Try to find the sub-directory to look in
                                DirectoryInfo newParent = null;
                                for (int ii = 0; ii < stages.Length; ++ii) {
                                    // If this directory is a match then we're good
                                    if (stages[ii].Name == searchStages[i]) {
                                        newParent = stages[ii];
                                        break;
                                    }
                                }

                                // If no directory was found then search ends here
                                if (newParent == null) break;

                                // If this is the last directory needed, then search is over
                                else if (i == searchStages.Length - 1) {
                                    foundDirectory = newParent;
                                    break;
                                }

                                // Otherwise, we need to go down to the next layer
                                else parent = newParent;
                            }
                        }
                    }

                    // If the directory was found then don't need to search anymore
                    if (foundDirectory != null) break;

                    // Add the sub-directories to the processing list
                    foreach (DirectoryInfo subDirectory in current.GetDirectories())
                        unsearched.Enqueue(subDirectory);
                }

                // Return the final directory path
                return (foundDirectory != null ?
                    foundDirectory.FullName.Substring(WORKING_DIRECTORY.Length).Replace('\\', '/') :
                    null
                );
            }

            /// <summary>
            /// Find all directories within the Assets folder that matches the search pattern
            /// </summary>
            /// <param name="searchDirectory">The directory that is being looked for</param>
            /// <param name="searchIdentifiedChildren">Flags if the children of an identified search directory should be searched</param>
            /// <returns>Returns a list of all of the asset directories contained within the project assets</returns>
            /// <remarks>
            /// A valid path that is returned by this search will start with "Assets/..."
            /// </remarks>
            public static List<string> FindAssetDirectories(string searchDirectory, bool searchIdentifiedChildren = false) {
                // Create the list of items to be found
                List<string> identifiedPaths = new List<string>();

                // If there is no directory, then we can't search for anything
                if (string.IsNullOrEmpty(searchDirectory)) return identifiedPaths;

                // Divide the specified search string into the different segments
                string[] searchStages = searchDirectory.Split(DIRECTORY_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);

                // If there are no stages then don't bother
                if (searchStages.Length == 0) return identifiedPaths;

                // Look for the chain of directories that are needed to identify the correct location
                Queue<DirectoryInfo> unsearched = new Queue<DirectoryInfo>();
                unsearched.Enqueue(new DirectoryInfo(WORKING_DIRECTORY + "Assets/"));

                // Process all of the contained directories for a match
                DirectoryInfo foundDirectory = null;
                while (unsearched.Count > 0) {
                    // Get the next directory to be processed
                    DirectoryInfo current = unsearched.Dequeue();

                    // If this directory is the start of the process, look for the sub sections
                    if (current.Name == searchStages[0]) {
                        // If this is the only level of the search then we're good
                        if (searchStages.Length == 1)
                            foundDirectory = current;

                        // Otherwise, look for the child directories that are needed
                        else {
                            // Store the parent directory that is currently being searched
                            DirectoryInfo parent = current;

                            // Look for each of the child stages that are needed
                            for (int i = 1; i < searchStages.Length; ++i) {
                                // Try to find the directory at this stage
                                DirectoryInfo[] stages = parent.GetDirectories(searchStages[i], SearchOption.TopDirectoryOnly);
                                if (stages.Length == 0) break;

                                // Try to find the sub-directory to look in
                                DirectoryInfo newParent = null;
                                for (int ii = 0; ii < stages.Length; ++ii) {
                                    // If this directory is a match then we're good
                                    if (stages[ii].Name == searchStages[i]) {
                                        newParent = stages[ii];
                                        break;
                                    }
                                }

                                // If no directory was found then search ends here
                                if (newParent == null) break;

                                // If this is the last directory needed, then search is over
                                else if (i == searchStages.Length - 1) {
                                    foundDirectory = newParent;
                                    break;
                                }

                                // Otherwise, we need to go down to the next layer
                                else parent = newParent;
                            }
                        }
                    }

                    // If the directory was found then don't need to search anymore
                    if (foundDirectory != null) {
                        // Add the path for this directory to the list
                        identifiedPaths.Add(foundDirectory.FullName.Substring(WORKING_DIRECTORY.Length).Replace('\\', '/'));
                        foundDirectory = null;

                        // If we don't need to search children we can cutout here
                        if (!searchIdentifiedChildren)
                            continue;
                    }

                    // Add the sub-directories to the processing list
                    foreach (DirectoryInfo subDirectory in current.GetDirectories())
                        unsearched.Enqueue(subDirectory);
                }

                // Return the collection of paths found
                return identifiedPaths;
            }

            ////////////////////////////////////////////////////////////////////////////////////////////////////
            //////////--------------------------------Asset Labelling---------------------------------//////////
            ////////////////////////////////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Add labels to the supplied asset, pulled from the supplied package data
            /// </summary>
            /// <param name="asset">The asset object that is to have the labels assigned to</param>
            /// <param name="packageData">The package data that is to have its labels added to the asset</param>
            public static void AddLabelsFromPackage(UnityEngine.Object asset, PackageData packageData) {
                if (packageData == null) return;
                AddLabelsToAsset(asset, packageData.Labels);
            }

            /// <summary>
            /// Remove labels from the supplied asset, pulled from the supplied package data
            /// </summary>
            /// <param name="asset">The asset object that is to have the labels removed from</param>
            /// <param name="packageData">The package data that is to have its labels removed from the asset</param>
            public static void RemoveLabelsFromPackage(UnityEngine.Object asset, PackageData packageData) {
                if (packageData == null) return;
                RemoveLabelsFromAsset(asset, packageData.Labels);
            }

            /// <summary>
            /// Add the specified labels to the supplied asset
            /// </summary>
            /// <param name="asset">The asset object that is to have the labels assigned to</param>
            /// <param name="labels">The collection of labels that are to be added to the asset</param>
            public static void AddLabelsToAsset(UnityEngine.Object asset, params string[] labels) { AddLabelsToAsset(asset, (IEnumerable<string>)labels); }

            /// <summary>
            /// Add the specified labels to the supplied asset
            /// </summary>
            /// <param name="asset">The asset object that is to have the labels assigned to</param>
            /// <param name="labels">The collection of labels that are to be added to the asset</param>
            public static void AddLabelsToAsset(UnityEngine.Object asset, IEnumerable<string> labels) {
                // Check there are labels to add
                if (labels == null) return;

                // Create a set for all of the labels that are needed
                HashSet<string> all = new HashSet<string>(labels);
                all.UnionWith(AssetDatabase.GetLabels(asset));

                // Add the labels to the asset
                string[] buffer = new string[all.Count];
                all.CopyTo(buffer);
                AssetDatabase.SetLabels(asset, buffer);
            }

            /// <summary>
            /// Remove the specified labels from the supplied asset
            /// </summary>
            /// <param name="asset">The asset object that is to have the labels removed</param>
            /// <param name="labels">The collection of labels that are to be removed from the asset</param>
            public static void RemoveLabelsFromAsset(UnityEngine.Object asset, params string[] labels) { RemoveLabelsFromAsset(asset, (IEnumerable<string>)labels); }

            /// <summary>
            /// Remove the specified labels from the supplied asset
            /// </summary>
            /// <param name="asset">The asset object that is to have the labels removed</param>
            /// <param name="labels">The collection of labels that are to be removed from the asset</param>
            public static void RemoveLabelsFromAsset(UnityEngine.Object asset, IEnumerable<string> labels) {
                // Check there are labels to add
                if (labels == null) return;

                // Create a set for all of the labels that are needed
                HashSet<string> all = new HashSet<string>(AssetDatabase.GetLabels(asset));
                all.ExceptWith(labels);

                // Set the labels for the asset
                string[] buffer = new string[all.Count];
                all.CopyTo(buffer);
                AssetDatabase.SetLabels(asset, buffer);
            }

            ////////////////////////////////////////////////////////////////////////////////////////////////////
            //////////-----------------------------Package Identification-----------------------------//////////
            ////////////////////////////////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Get the data for the package that has the supplied name
            /// </summary>
            /// <param name="name">The name of the package that is to be retrieved</param>
            /// <param name="ignoreCase">Flags if the case of the text should be ignored for this search</param>
            /// <returns>Returns a Package Data object containing information about the identified package or null if unable to find</returns>
            public static PackageData GetPackageFromName(string name, bool ignoreCase = false) {
                // If there is no name supplied, we won't find anything
                if (string.IsNullOrEmpty(name)) return null;

                // Start the search for the package data
                if (ignoreCase) {
                    // Lower the name for consistency
                    name = name.ToLowerInvariant();

                    // Search for a matching name
                    for (int i = 0; i < includedPackages.Count; ++i) {
                        if (includedPackages[i].Name.ToLowerInvariant() == name)
                            return includedPackages[i];
                    }
                }

                // Otherwise, straight up search for matching name
                else {
                    for (int i = 0; i < includedPackages.Count; ++i) {
                        if (includedPackages[i].Name == name)
                            return includedPackages[i];
                    }
                }

                // If we got this far, nothing to return
                return null;
            }

            /// <summary>
            /// Get the data for the package that the supplied value type belongs to
            /// </summary>
            /// <param name="obj">The object that is to have it's package recovered from</param>
            /// <returns>Returns a PackageData object containing information about the identified package or null if unable to find a match</returns>
            /// <remarks>
            /// For this function to work, the type must belong to an Assembly definition that could be identified
            /// and associated with a package description
            /// </remarks>
            public static PackageData GetPackageFromType(object obj) { return GetPackageFromType(obj.GetType()); }

            /// <summary>
            /// Get the data for the package that the supplied value type belongs to
            /// </summary>
            /// <param name="type">A type object that is to be looked for in the packages to get the matching package data</param>
            /// <returns>Returns a PackageData object containing information about the identified package or null if unable to find a match</returns>
            /// <remarks>
            /// For this function to work, the type must belong to an Assembly definition that could be identified
            /// and associated with a package description
            /// </remarks>
            public static PackageData GetPackageFromType(Type type) {
                // Look for the type in the identified packages
                for (int i = 0; i < includedPackages.Count; ++i) {
                    // Check that there is an assembly file to search
                    if (includedPackages[i].PackageAssembly != null) {
                        // Look for the type in the assembly
                        Type found = includedPackages[i].PackageAssembly.GetType(type.FullName, false);

                        // If the type could be found, it belongs to this assembly
                        if (found != null) return includedPackages[i];
                    }
                }

                // If got this far, then we have nothing
                return null;
            }

            /// <summary>
            /// Get the Package Data associated with the specified <see cref="AssemblyDefinitionAsset"/>
            /// </summary>
            /// <param name="asset">The assembly asset that is to be looked for in the contained package descriptions</param>
            /// <returns>Returns a PackageData object containing information about the identified package or null if unable to find a match</returns>
            public static PackageData GetPackageFromAssemblyAsset(AssemblyDefinitionAsset asset) {
                // If there is no asset, nothing to find
                if (!asset) return null;

                // Look for a matching data object
                for (int i = 0; i < includedPackages.Count; ++i) {
                    if (includedPackages[i].AssemblyAsset == asset)
                        return includedPackages[i];
                }

                // If we got this far, nothing to find
                return null;
            }

            ////////////////////////////////////////////////////////////////////////////////////////////////////
            //////////----------------------------------Menu Actions----------------------------------//////////
            ////////////////////////////////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Remove the MCU registry scripting defines from the project
            /// </summary>
            /// <remarks>
            /// This will cause an assembly reload which will result in <see cref="ApplyAdapterDefines"/> being raised again
            /// </remarks>
            [MenuItem("MCU/Registry/Remove Adapter Defines")]
            public static void RemoveAdapterDefines() {
                // Get the scripting symbols defined for the current platform
                string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup
                );

                // Find all entries that need to persist
                bool modified = false;
                HashSet<string> persistingDefines = new HashSet<string>();
                string[] individuals = symbols.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < individuals.Length; ++i) {
                    // If the symbol doesn't start with the registry stamp it can stay
                    if (!individuals[i].StartsWith(REGISTERY_SCRIPT_DEFINE_PREFIX))
                        persistingDefines.Add(individuals[i]);

                    // Otherwise, there will be changes
                    else if (!modified)
                        modified = true;
                }

                // If the entries will be changed, set the remainging
                if (modified) {
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(
                        EditorUserBuildSettings.selectedBuildTargetGroup,
                        string.Join(";", persistingDefines)
                    );
                }
            }

            /// <summary>
            /// Identify the scripting define symbols that should be included in the project settings for the current adaptors
            /// </summary>
            [MenuItem("MCU/Registry/Apply Adapter Defines")]
            public static void ApplyAdapterDefines() {
                // If the registry couldn't be initialised properly, can't assign symbols
                if (!Initialized) {
                    Debug.LogError($"Unable to assign scripting defines as ");
                    return;
                }

                // Get the scripting symbols defined for the current platform
                string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup
                );

                // Find all of the symbols that need to be included in the project settings
                HashSet<string> scriptingDefines = new HashSet<string>();
                foreach (AdapterInformation adapterInfo in adapters.Values) {
                    // If no scripting define set for the adapter, no point
                    if (string.IsNullOrWhiteSpace(adapterInfo.scriptingDefineIdentifier))
                        continue;

                    // If the symbol is already included in the list, don't bother
                    if (scriptingDefines.Contains(adapterInfo.scriptingDefineIdentifier))
                        continue;

                    // Otherwise, missing symbol needs including
                    scriptingDefines.Add(REGISTERY_SCRIPT_DEFINE_PREFIX + adapterInfo.scriptingDefineIdentifier);
                }

                // Get the current collection of scripting defined symbols
                int previousCount = 0;
                bool wasModified = false;
                HashSet<string> previousDefines = new HashSet<string>();
                string[] individuals = symbols.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < individuals.Length; ++i) {
                    // If this isn't one from MCU, add it to the list
                    if (!individuals[i].StartsWith(REGISTERY_SCRIPT_DEFINE_PREFIX))
                        previousDefines.Add(individuals[i]);

                    // Otherwise, check if something is being added or removed
                    else {
                        ++previousCount;
                        if (!scriptingDefines.Contains(individuals[i]))
                            wasModified = true;
                    }
                }

                // Assign the new symbols to the project settings
                wasModified |= (previousCount != scriptingDefines.Count);
                if (wasModified) {
                    // Copy the non-registry symbols into the main list so that they appear after
                    scriptingDefines.UnionWith(previousDefines);

                    // Set the scripting define symbols as needed
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(
                        EditorUserBuildSettings.selectedBuildTargetGroup,
                        string.Join(";", scriptingDefines)
                    );
                }
            }

            /// <summary>
            /// Find all of the MCU assets in the project, and apply the required labels to them
            /// </summary>
            [MenuItem("MCU/Registry/Apply Package Labels")]
            public static void ApplyPackageLabels() {
                // Find all of the MCU directories within the assets of the project
                List<string> rootDirectories = FindAssetDirectories(nameof(MCU), false);
                if (rootDirectories.Count == 0) return;

                // Queue of the processing of labels for elements
                Queue<(DirectoryInfo, HashSet<string>)> unsearched = new Queue<(DirectoryInfo, HashSet<string>)>();
                for (int i = 0; i < rootDirectories.Count; ++i)
                    unsearched.Enqueue((new DirectoryInfo(rootDirectories[i]), new HashSet<string>()));

                // Label all of the identified assets that are found
                List<FileSystemInfo> fileBuffer = new List<FileSystemInfo>();
                while (unsearched.Count > 0) {
                    // Get the search element for this stage
                    (DirectoryInfo dir, HashSet<string> labels) current = unsearched.Dequeue();

                    // Add the current directories name to the label list
                    current.labels.Add(current.dir.Name);

                    // Load in the labels for the package that is found
                    bool foundPackageRoot = false;
                    FileInfo[] assemblyFiles = current.dir.GetFiles("*.asmdef", SearchOption.TopDirectoryOnly);
                    foreach (FileInfo assemblyFile in assemblyFiles) {
                        // Try to load the assembly reference at this location
                        AssemblyDefinitionAsset assemblyAsset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(
                            assemblyFile.FullName.Substring(WORKING_DIRECTORY.Length).Replace('\\', '/')
                        );

                        // If an asset could be loaded, get the labels that need to be added
                        if (assemblyAsset) {
                            // Flag that a package root was found
                            foundPackageRoot = true;

                            // Look for the package data that is to be associated with it
                            PackageData packageData = GetPackageFromAssemblyAsset(assemblyAsset);
                            if (packageData == null) {
                                Debug.LogError($"Unable to find the PackageData corresponding to '{assemblyAsset}'. Can't extract labels", assemblyAsset);
                                continue;
                            }

                            // Add the current level of labels to the package data
                            packageData.Labels.UnionWith(current.labels);

                            // Add the labels to the list for this level
                            current.labels.UnionWith(packageData.Labels);
                        }
                    }

                    // Clear the buffer of files for this point
                    fileBuffer.Clear();

                    // If we didn't find a package root, we need to recurse down
                    if (!foundPackageRoot) {
                        // Find all of the directories that are at this level
                        foreach (DirectoryInfo dir in current.dir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                            unsearched.Enqueue((dir, new HashSet<string>(current.labels)));

                        // Find the files at this level that need to be labeled
                        fileBuffer.AddRange(current.dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly));
                    }

                    // Otherwise, we just want to grab everything in this directory and below to be labeled
                    else fileBuffer.AddRange(current.dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories));

                    // Add this directory to the collection to be labeled
                    fileBuffer.Add(current.dir);

                    // Set the labels on all of the assets that were identified
                    foreach (FileSystemInfo element in fileBuffer) {
                        // Correct the path of the asset to something that works with the Asset Database
                        string assetPath = element.FullName.Substring(WORKING_DIRECTORY.Length).Replace('\\', '/');

                        // Try to load the generic asset at this location to set the labels
                        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        if (asset) AddLabelsToAsset(asset, current.labels);
                    }
                }
            }
        }
    }
}
#endif