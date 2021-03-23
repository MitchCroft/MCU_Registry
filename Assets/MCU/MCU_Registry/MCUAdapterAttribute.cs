using System;

namespace MCU.Registry {
    /// <summary>
    /// Mark a type as a MCU Package Adapter that should be recorded by the <see cref="MCURegistry"/> interface
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class MCUAdapterAttribute : Attribute {
        /*----------Properties----------*/
        //PUBLIC
    
        /// <summary>
        /// The name that will be used to access the nominated adapter functionality
        /// </summary>
        public string AdapterName { get; private set; }

        /// <summary>
        /// The version of the adapter that is currently in use
        /// </summary>
        public string Version { get; private set; }

        /// <summary>
        /// The scripting define symbol that will be used for the adapter
        /// </summary>
        /// <remarks>
        /// If not defined, no scripting define symbol will be defined
        /// </remarks>
        public string ScriptingDefineIdentifier { get; private set; }

        /*----------Functions----------*/
        //PUBLIC

        /// <summary>
        /// Initialise this object with it's base values
        /// </summary>
        /// <param name="adapterName">The name that will be used to access the nominated adapter functionality</param>
        /// <param name="version">The version of the adapter that is currently in use</param>
        /// <param name="scriptingDefineIdentifier">The scripting define symbol that will be used for the package</param>
        public MCUAdapterAttribute(string adapterName, string version, string scriptingDefineIdentifier = "") {
            AdapterName = !string.IsNullOrWhiteSpace(adapterName) ? adapterName.Trim() : throw new NullReferenceException("Adapter Name must be defined for dynamic access");
            Version = !string.IsNullOrWhiteSpace(version) ? version.Trim() : throw new NullReferenceException("Version must be be defined for dynamic access");
            ScriptingDefineIdentifier = (string.IsNullOrWhiteSpace(scriptingDefineIdentifier) ? string.Empty : scriptingDefineIdentifier.Trim());
        }
    }
}