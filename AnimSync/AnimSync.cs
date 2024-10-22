using System;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.IoC;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.Havok.Animation.Rig;

namespace AnimSync;

public class AnimSync: IDalamudPlugin {
	public string Name => "Animation Syncer";
	
	[PluginService] public static IDalamudPluginInterface Interface  {get; private set;} = null!;
	[PluginService] public static ICommandManager         Commands   {get; private set;} = null!;
	[PluginService] public static IPluginLog              Logger     {get; private set;} = null!;
	[PluginService] public static IObjectTable            Objects    {get; private set;} = null!;
	[PluginService] public static IGameInteropProvider    HookProv   {get; private set;} = null!;
	[PluginService] public static ISigScanner             SigScanner {get; private set;} = null!;
	
	private const string command = "/animsync";
	private const float MAXDIST = 0.25f;
	private const float MAXROT = (float)Math.PI / 4; // 45 degrees
	
	private unsafe delegate void AnimRootDelegate(hkaPose* pose);
	private static Hook<AnimRootDelegate> AnimRootHook = null!;
	
	private static Dictionary<nint, (Vector3, Quaternion, nint)> RootSyncs = new();
	
	public unsafe AnimSync() {
	 // AnimRootHook = HookProv.HookFromAddress<AnimRootDelegate>(SigScanner.ScanText("48 83 EC 08 8B 02"), AnimRoot);
		AnimRootHook = HookProv.HookFromAddress<AnimRootDelegate>(SigScanner.ScanText("48 83 EC 18 80 79 ?? 00"), AnimRoot);
		AnimRootHook.Enable();
		
		Commands.AddHandler(command, new CommandInfo(OnCommand) {
			HelpMessage = "Resets all player and battlenpc animations to 0"
		});
	}
	
	public void Dispose() {
		AnimRootHook.Dispose();
		Commands.RemoveHandler(command);
	}
		
	private unsafe void OnCommand(string cmd, string args) {
		if(cmd != command)
			return;
		
		foreach(var obj in Objects) {
			if(IsValidObject(obj)) {
				var actor = (Actor*)obj.Address;
				actor->Control->hkaAnimationControl.LocalTime = 0;
			}
		}
	}
	
	private unsafe void AnimRoot(hkaPose* pose) {
		AnimRootHook.Original(pose);
		
		lock(RootSyncs) {
			if(RootSyncs.TryGetValue((nint)pose, out var sync)) {
				var skeleton = (Skeleton*)sync.Item3;
				skeleton->Transform.Position = sync.Item1;
				skeleton->Transform.Rotation = sync.Item2;
			}
		}
	}
	
	private unsafe bool IsValidObject(IGameObject obj) {
		return (obj.ObjectKind == ObjectKind.BattleNpc || obj.ObjectKind == ObjectKind.Player) && ((Actor*)obj.Address)->Control != null;
	}
}