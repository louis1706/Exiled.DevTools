﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Exiled.API.Enums;
using Exiled.API.Features;
using HarmonyLib;
using MonoMod.Utils;

namespace Exiled.DevTools
{
	public sealed class DevTools : Plugin<Config>
	{
		public override string Name => "Exiled.DevTools";
		public override string Author => "sanyae2439";
		public override string Prefix => "exiled_devtools";
		public override PluginPriority Priority => PluginPriority.Highest;
		public override Version Version => new Version(Assembly.GetName().Version.Major, Assembly.GetName().Version.Minor, Assembly.GetName().Version.Build);
		public override Version RequiredExiledVersion => new Version(2, 8, 0);

		public static DevTools Instance { get; private set; }
		public Harmony Harmony { get; private set; }

		private readonly Dictionary<EventInfo, Delegate> _DynamicHandlers = new Dictionary<EventInfo, Delegate>();

		public override void OnEnabled()
		{
			Instance = this;

			AddEventHandlers();
			RegistPatch();

			base.OnEnabled();
		}

		public override void OnDisabled()
		{
			Instance = null;

			RemoveEventHandlers();
			UnRegistPatch();
	
			base.OnDisabled();
		}

		private void AddEventHandlers()
		{
			foreach(var eventClass in Events.Events.Instance.Assembly.GetTypes().Where(x => x.Namespace == "Exiled.Events.Handlers"))
				foreach(EventInfo eventInfo in eventClass.GetEvents())
				{
					if(this.Config.DisabledLoggingEvents.Contains(eventInfo.Name)) continue;

					Delegate handler = null;
					if(eventInfo.EventHandlerType.GenericTypeArguments.Any())
						handler = typeof(DevTools)
							.GetMethod(nameof(DevTools.MessageHandler))
							.MakeGenericMethod(eventInfo.EventHandlerType.GenericTypeArguments[0])
							.CreateDelegate(typeof(Events.Events.CustomEventHandler<>).MakeGenericType(eventInfo.EventHandlerType.GenericTypeArguments[0]));
					else
						handler = typeof(DevTools)
							.GetMethod(nameof(DevTools.MessageHandlerForEmptyArgs))
							.CreateDelegate<Events.Events.CustomEventHandler>();
					eventInfo.AddEventHandler(null, handler);
					this._DynamicHandlers.Add(eventInfo, handler);
				}
		}

		private void RemoveEventHandlers()
		{
			foreach(var eventClass in Events.Events.Instance.Assembly.GetTypes().Where(x => x.Namespace == "Exiled.Events.Handlers"))
				foreach(EventInfo eventInfo in eventClass.GetEvents())
					if(this._DynamicHandlers.ContainsKey(eventInfo))
					{
						eventInfo.RemoveEventHandler(null, this._DynamicHandlers[eventInfo]);
						this._DynamicHandlers.Remove(eventInfo);
					}
		}

		private void RegistPatch()
		{
			try
			{
				Harmony = new Harmony(this.Name + DateTime.Now.Ticks);
				Harmony.PatchAll();
			}
			catch(Exception ex)
			{
				Log.Error($"Patching Failed : {ex}");
			}
		}

		private void UnRegistPatch()
		{
			try
			{
				Harmony.UnpatchAll();
				Harmony = null;
			}
			catch(Exception ex)
			{
				Log.Error($"Unpatching Failed : {ex}");
			}
		}

		public static void MessageHandler<T>(T ev) where T : EventArgs
		{
			string message = $"[{ev.GetType().Name.Replace("EventArgs", string.Empty)}]\n";
			foreach(var propertyInfo in ev.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				try
				{
					message += $"{propertyInfo.Name} : {propertyInfo.GetValue(ev)}\n";
				}
				catch(Exception e)
				{
					message += $"{propertyInfo.Name} : Error[{e.Message}]\n";
				}

				if(DevTools.Instance.Config.DisabledLoggingClassNameForNest.Contains(propertyInfo.PropertyType.FullName)) continue;

				if(propertyInfo.PropertyType.IsClass || (propertyInfo.PropertyType.IsValueType && !propertyInfo.PropertyType.IsPrimitive && !propertyInfo.PropertyType.IsEnum)) 
				{
					
					bool isString = propertyInfo.PropertyType.Name == nameof(System.String);
					bool isEnumerable = propertyInfo.PropertyType.GetInterfaces().Any(t => t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));

					if(!isString && !isEnumerable)
					{
						foreach(var propertyInClass in propertyInfo.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
						{
							if(propertyInClass.GetIndexParameters().Length > 0) continue;

							try
							{
								message += $"    {propertyInClass.Name} : {propertyInClass.GetValue(propertyInfo.GetValue(ev))}\n";

							}
							catch(Exception)
							{
								message += $"    {propertyInClass.Name} : null\n";
							}
						}

						foreach(var fieldInClass in propertyInfo.PropertyType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
						{
							try
							{
								message += $"    {fieldInClass.Name} : {fieldInClass.GetValue(propertyInfo.GetValue(ev))}\n";
							}
							catch(Exception)
							{
								message += $"    {fieldInClass.Name} : null\n";
							}
						}
					}

					if(isEnumerable && !isString)
					{
						int counter = 0;
						var enumerable = (IEnumerable)propertyInfo.GetValue(ev);

						if(propertyInfo.PropertyType.IsGenericType)
						{
							var genericparam = propertyInfo.PropertyType.GetGenericArguments()[0];
							var count = typeof(System.Linq.Enumerable).GetMethods().First(x => x.Name == nameof(System.Linq.Enumerable.Count) && x.IsStatic && x.GetParameters().Length == 1).MakeGenericMethod(genericparam);
							message += $"    Length : {count.Invoke(null, new object[] { enumerable })}\n";
						}
						foreach(var item in enumerable)
							message += $"    [{counter++}] : {item}\n";
					}
				}
			}
			Log.Debug(message.TrimEnd('\n'));
		}

		public static void MessageHandlerForEmptyArgs(Events.Events.CustomEventHandler _) => Log.Debug($"[{new StackFrame(2).GetMethod().Name}]");
	}
}