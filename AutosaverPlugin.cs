using System;
using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

[PluginType(Id = "com.veasu.autosaver", Name = "Autosaver Node", Version = "0.1.2", Author = "Veasu", Description = "Adds an autosaver node",
    NodeTypes = new[] { typeof(AutosaverNode) })]
public class AutosaverPlugin : Plugin {
    protected override void OnCreate() {
        base.OnCreate();
    }

    protected override void OnDestroy() {
        base.OnDestroy();
    }

    public override void OnPreUpdate() {
        base.OnPreUpdate();
    }
}