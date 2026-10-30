using Mono.Addins;

// Mono.Addins requires assembly-level registration in addition to the
// [Extension] attribute on the region-module class.
// Match the OpenSim 0.9.3.1 host add-in dependency for the M0 test build.
[assembly: Addin("OpenSim.MQTT", "0.9.3.1")]
[assembly: AddinDependency("OpenSim.Region.Framework", "0.9.3.1")]
