using System;
using System.Reflection;
using System.Collections.Generic;

namespace MCU.Registry {
    /// <summary>
    /// Manage the registration of different MCU adapters that can be used dynamically
    /// </summary>
    [MCUAdapter("registry", "1.0.0", "REG")]
    public static class MCURegistry {
        /*----------Types----------*/
        //PRIVATE

        /// <summary>
        /// Store information about identified adapter functions that can be raised
        /// </summary>
        private sealed class AdapterInformation {
            /// <summary>
            /// Map the identified functions to the names that they have been assigned for use
            /// </summary>
            public Dictionary<string, MethodInfo> adapterMethods = new Dictionary<string, MethodInfo>();

            /// <summary>
            /// The version of the adapter that is currently loaded
            /// </summary>
            public string adapterVersion = string.Empty;

#if UNITY_EDITOR
            /// <summary>
            /// The scripting identifier that is to be setup for the adapter
            /// </summary>
            public string scriptingDefineIdentifier = string.Empty;
#endif
        }

        /*----------Variables----------*/
        //CONST

        /// <summary>
        /// The prefix that will be prepended to the scripting defines of nominated adapters 
        /// </summary>
        /// <remarks>
        /// A consistent prefix allows for the updating of scripting defines automatically as
        /// packages are added/removed/updated
        /// </remarks>
        private const string REGISTERY_SCRIPT_DEFINE_PREFIX = "MCU_";

        //PRIVATE

        /// <summary>
        /// Store all of the adapters that can be accessed and used via this registry
        /// </summary>
        private static Dictionary<string, AdapterInformation> adapters;

        /*----------Properties----------*/
        //PUBLIC

        /// <summary>
        /// Flag if the registry was initialised successfully
        /// </summary>
        public static bool Initialized { get; private set; }

        /*----------Functions----------*/
        //PRIVATE

        /// <summary>
        /// Identify the elements within the project that can be called dynamically
        /// </summary>
        static MCURegistry() {
            // Find all of the adapters within the project
            adapters = new Dictionary<string, AdapterInformation>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                foreach (Type type in assembly.GetTypes()) {
                    // Look for an adapter attribute on the type
                    MCUAdapterAttribute adapterAtt = type.GetCustomAttribute<MCUAdapterAttribute>();
                    if (adapterAtt == null)
                        continue;

                    // There must be an adapter name defined
                    if (string.IsNullOrWhiteSpace(adapterAtt.AdapterName))
                        continue;

                    // If there are multiple instances of the same adapter name, they *must* have matching information
                    if (adapters.ContainsKey(adapterAtt.AdapterName)) {
                        AdapterInformation existing = adapters[adapterAtt.AdapterName];
                        if (existing.adapterVersion != adapterAtt.Version
#if UNITY_EDITOR
                            || existing.scriptingDefineIdentifier != adapterAtt.ScriptingDefineIdentifier
#endif
                            )
                            throw new Exception($"Multiple instances of Adapters defined with the name '{adapterAtt.AdapterName}' with differing information. These must match");
                    }

                    // If there is no adapter defined for this name, create it
                    else {
                        adapters[adapterAtt.AdapterName] = new AdapterInformation {
                            adapterVersion = adapterAtt.Version,
#if UNITY_EDITOR
                            scriptingDefineIdentifier = adapterAtt.ScriptingDefineIdentifier
#endif
                        };
                    }

                    // Get the information object that will store the identified methods
                    AdapterInformation adapterInfo = adapters[adapterAtt.AdapterName];

                    // Find all of the methods that can be called
                    MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (MethodInfo method in methods) {
                        // Look for an adapter method attribute
                        MCUAdapterMethodAttribute methodAtt = method.GetCustomAttribute<MCUAdapterMethodAttribute>();
                        if (methodAtt == null)
                            continue;

                        // Find the name that will be used for the dynamic access of the method
                        string name = (!string.IsNullOrWhiteSpace(methodAtt.FunctionName) ?
                            methodAtt.FunctionName :
                            method.Name
                        );

#if UNITY_EDITOR
                        // If there is already a method defined then warn of override
                        if (adapterInfo.adapterMethods.ContainsKey(name))
                            UnityEngine.Debug.LogWarning($"The Adapter ({adapterAtt.AdapterName}) method with assigned name '{name}' has multiple definitions. The previous method '{GetMethodSignature(adapterInfo.adapterMethods[name])}' will be replaced with '{GetMethodSignature(method)}'");
#endif

                        // Store the method for use
                        adapterInfo.adapterMethods[name] = method;
                    }
                }
            }

            Initialized = true;
        }

        /// <summary>
        /// Force an exception to be raised if the registry hasn't been initialised properly
        /// </summary>
        private static void ThrowIfNotInitialized() {
            if (!Initialized)
                throw new Exception("MCURegistry has not been initialised successfully");
        }

#if UNITY_EDITOR
        /// <summary>
        /// Identify the scripting define symbols that need to be included within the project settings
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void AssignScriptingDefines() {
            // If the registry couldn't be initialised properly, can't assign symbols
            if (!Initialized)
                return;

            // Get the scripting symbols defined for the current platform
            string symbols = UnityEditor.PlayerSettings.GetScriptingDefineSymbolsForGroup(
                UnityEditor.EditorUserBuildSettings.selectedBuildTargetGroup
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
                UnityEditor.PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    UnityEditor.EditorUserBuildSettings.selectedBuildTargetGroup,
                    string.Join(";", scriptingDefines)
                );
            }
        }

        /// <summary>
        /// Populate a method signature for logging
        /// </summary>
        /// <param name="method">The MethodInfo object that will have the signature generated</param>
        /// <returns>Returns a string that can be displayed</returns>
        private static string GetMethodSignature(MethodInfo method) {
            // Compile the signature string
            System.Text.StringBuilder sb = new System.Text.StringBuilder(
                $"{method.DeclaringType.FullName}.{method.Name}"
            );

            // Get the parameters that are listed for the method
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length > 0) {
                sb.Append('(');

                // Add all of the parameters to the signature
                for (int i = 0; i < parameters.Length; ++i) {
                    // Specify the parameter type and name
                    sb.Append($"{parameters[i].ParameterType.Name} {parameters[i].Name}");

                    // Seperate the parameters as required
                    if (i < parameters.Length - 1)
                        sb.Append(", ");
                }

                sb.Append(')');
            }

            return sb.ToString();
        }
#endif

        //PUBLIC

        /// <summary>
        /// Check to see if the registry has an adapter for the specified adapter name
        /// </summary>
        /// <param name="adapterName">The name of the adapter that is being looked for</param>
        /// <returns>Returns true if the registry has a adapter with the specified name</returns>
        public static bool HasAdapter(string adapterName) {
            ThrowIfNotInitialized();
            return adapters.ContainsKey(adapterName);
        }

        /// <summary>
        /// Check to see if the registry has an adapter and method with the specified name
        /// </summary>
        /// <param name="adapterName">The name of the adapter that is being looked for</param>
        /// <param name="methodName">The method that is being looked for within the adapter</param>
        /// <returns>Returns true if the specified adapter exists and has the listed method</returns>
        public static bool HasAdapterWithMethod(string adapterName, string methodName) {
            ThrowIfNotInitialized();
            return adapters.ContainsKey(adapterName) && 
                   adapters[adapterName].adapterMethods.ContainsKey(methodName);
        }

        /// <summary>
        /// Get the version identifier for the adapter with the specified adapter name
        /// </summary>
        /// <param name="adapterName">The name of the adapter that is being looked for</param>
        /// <returns>Returns the nominated version string or null if no adapter registered</returns>
        public static string GetAdapterVersion(string adapterName) {
            ThrowIfNotInitialized();
            return (adapters.ContainsKey(adapterName) ?
                adapters[adapterName].adapterVersion :
                null
            );
        }

        /// <summary>
        /// Raise the specified adapter method 
        /// </summary>
        /// <param name="adapterName">The name of the adapter that is to be accessed</param>
        /// <param name="methodName">The name of the method that is to be raised</param>
        /// <param name="returnVal">Passes out the return value, or null if unable to successfully raise method</param>
        /// <param name="parameters">The assortment of parameters that are to be passed to the called method</param>
        /// <returns>Returns true if the adapter method was called successfully, otherwise false</returns>
        public static bool CallAdapterMethod(string adapterName, string methodName, out object returnVal, params object[] parameters) {
            // Adapters must of been setup properly
            ThrowIfNotInitialized();

            // Default set return value
            returnVal = null;

            // Check that the method can be called
            if (!HasAdapterWithMethod(adapterName, methodName))
                return false;

            // Try to raise the method
            try {
                returnVal = adapters[adapterName].adapterMethods[methodName].Invoke(null, parameters);
                return true;
            }

            // If anything goes wrong, couldn't call the method
            catch { return false; }
        }

        /// <summary>
        /// Raise the specified adapter method
        /// </summary>
        /// <typeparam name="T">The anticipated return type of the method that is being called</typeparam>
        /// <param name="adapterName">The name of the adapter that is to be accessed</param>
        /// <param name="methodName">The name of the method that is to be raised</param>
        /// <param name="returnVal">Passes out the return value, or the type default if the method couldn't be raised or the return value doesn't match the type</param>
        /// <param name="parameters">The assortment of parameters that are to be passed to the called method</param>
        /// <returns>Returns true if the adapter method was called successfully, otherwise false</returns>
        public static bool CallAdapterMethod<T>(string adapterName, string methodName, out T returnVal, params object[] parameters) {
            // Adapters must of been setup properly
            ThrowIfNotInitialized();

            // Default set return value
            returnVal = default;

            // Check that the method can be called
            if (!HasAdapterWithMethod(adapterName, methodName))
                return false;

            // Try to raise the method
            try {
                returnVal = (T)adapters[adapterName].adapterMethods[methodName].Invoke(null, parameters);
                return true;
            }

            // If anything goes wrong, couldn't call the method
            catch { return false; }
        }

        /// <summary>
        /// Raise the specified adapter method 
        /// </summary>
        /// <param name="adapterName">The name of the adapter that is to be accessed</param>
        /// <param name="methodName">The name of the method that is to be raised</param>
        /// <param name="parameters">The assortment of parameters that are to be passed to the called method</param>
        /// <returns>Returns the returning value from the method or null if unable to raise</returns>
        public static object CallAdapterMethod(string adapterName, string methodName, params object[] parameters) {
            object returnVal;
            return (CallAdapterMethod(adapterName, methodName, out returnVal, parameters) ?
                returnVal :
                null
            );
        }

        /// <summary>
        /// Raise the specified adapter method 
        /// </summary>
        /// <typeparam name="T">The anticipated return type of the method that is being called</typeparam>
        /// <param name="adapterName">The name of the adapter that is to be accessed</param>
        /// <param name="methodName">The name of the method that is to be raised</param>
        /// <param name="parameters">The assortment of parameters that are to be passed to the called method</param>
        /// <returns>Returns the returning value from the method or the type default if the method couldn't be raised or the return value doesn't match the type</returns>
        public static T CallAdapterMethod<T>(string adapterName, string methodName, params object[] parameters) {
            T returnVal;
            return (CallAdapterMethod(adapterName, methodName, out returnVal, parameters) ?
                returnVal :
                default
            );
        }

        /// <summary>
        /// Raise the specified adapter method 
        /// </summary>
        /// <param name="adapterName">The name of the adapter that is to be accessed</param>
        /// <param name="methodName">The name of the method that is to be raised</param>
        /// <param name="defaultReturn">The default return value that will be used if the method can't be invoked</param>
        /// <param name="parameters">The assortment of parameters that are to be passed to the called method</param>
        /// <returns>Returns the returning value from the method or the supplied default return</returns>
        public static object CallAdapterMethod(string adapterName, string methodName, object defaultReturn, params object[] parameters) {
            object returnVal;
            return (CallAdapterMethod(adapterName, methodName, out returnVal, parameters) ?
                returnVal :
                defaultReturn
            );
        }

        /// <summary>
        /// Raise the specified adapter method 
        /// </summary>
        /// <typeparam name="T">The anticipated return type of the method that is being called</typeparam>
        /// <param name="adapterName">The name of the adapter that is to be accessed</param>
        /// <param name="methodName">The name of the method that is to be raised</param>
        /// <param name="defaultReturn">The default return value that will be used if the method can't be invoked</param>
        /// <param name="parameters">The assortment of parameters that are to be passed to the called method</param>
        /// <returns>Returns the returning value from the method or the supplied default return</returns>
        public static T CallAdapterMethod<T>(string adapterName, string methodName, T defaultReturn, params object[] parameters) {
            T returnVal;
            return (CallAdapterMethod(adapterName, methodName, out returnVal, parameters) ?
                returnVal :
                defaultReturn
            );
        }
    }
}