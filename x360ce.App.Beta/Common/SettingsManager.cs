﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using SharpDX.DirectInput;
using x360ce.App.Controls;
using System.Linq;
using x360ce.Engine.Data;
using x360ce.Engine;
using System.Linq.Expressions;
using System.Reflection;
using System.ComponentModel;
using System.IO;

namespace x360ce.App
{
	public partial class SettingsManager
	{

		// Products  - DInput Devices.
		// Programs  - Games of all Users.
		// Settings  - Links Product, Game and PadSettings.
		// Summaries - Summary of Settings of all Users.
		// UserControllers - Detail information about user Controllers.
		//
		//            [Vendor]    
		//             ↓
		//            [Product]   (export) → [Summaries]
		//                   ↓     ↑
		//  [PadSetting] ←  [Setting] → [Games] → (export) → [Programs]
		//

		static object OptionsLock = new object();

		/// <summary>x360ce Options</summary>
		public static XSettingsData<Options> OptionsData
		{
			get
			{
				lock (OptionsLock)
				{
					if (_OptionsData == null)
					{
						_OptionsData = new XSettingsData<Options>("Options.xml", "x360ce Options");
						_OptionsData.Load();
						if (_OptionsData.Items.Count == 0)
						{
							var o = new Options();
							_OptionsData.Items.Add(o);
						}
						_OptionsData.Items[0].InitDefaults();
					}
					return _OptionsData;
				}
			}
		}
		public static XSettingsData<Options> _OptionsData;

		public static Options Options { get { return OptionsData.Items[0]; } }

		// Global settings.

		/// <summary>Programs - Default settings for most popular Games.</summary>
		/// <remarks>Used to create PDB file.</remarks>
		public static XSettingsData<Engine.Data.Program> Programs = new XSettingsData<Engine.Data.Program>("Programs.xml", "Default settings for most popular Games.");

		/// <summary>Summaries - Default PadSettings for most popular Game, Device and Controller combination.</summary>
		public static XSettingsData<Engine.Data.Summary> Summaries = new XSettingsData<Engine.Data.Summary>("Summaries.xml", "Default PadSettings for most popular Game, Device and Controller combination.");

		/// <summary>Presets - Default PadSettings for most popular Devices (Products).</summary>
		public static XSettingsData<Preset> Presets = new XSettingsData<Preset>("Presets.xml", "Default PadSettings for most popular Devices (Products).");

		/// <summary>MapNames - Most popular layouts for Games.</summary>
		public static XSettingsData<Layout> Layouts = new XSettingsData<Layout>("Layouts.xml", "Most popular layouts for Games.");

		// User settings.

		/// <summary>User Games.</summary>
		public static XSettingsData<Engine.Data.UserGame> UserGames = new XSettingsData<Engine.Data.UserGame>("UserGames.xml", "User Games.");

		/// <summary>User Devices (Direct Input).</summary>
		public static XSettingsData<Engine.Data.UserDevice> UserDevices = new XSettingsData<Engine.Data.UserDevice>("UserDevices.xml", "User Devices (Direct Input).");

		/// <summary>Setting contains link between Game, Device (DInput), PadSetting and Controller (XInput)</summary>
		public static XSettingsData<Engine.Data.Setting> Settings = new XSettingsData<Engine.Data.Setting>("Settings.xml", "User Settings.");

		/// <summary>User Instances.</summary>
		public static XSettingsData<Engine.Data.UserInstance> UserInstances = new XSettingsData<Engine.Data.UserInstance>("UserInstances.xml", "User Controller Instances. Maps same device to multiple instance GUIDs it has on multiple PCs.");

		/// <summary>User Computers.</summary>
		public static XSettingsData<Engine.Data.UserComputer> UserComputers = new XSettingsData<Engine.Data.UserComputer>("UserComputers.xml", "User Computers. Maps login to user computers.");

		// Property below is shared between User and Global settings:

		/// <summary>Contains PadSettings for Summaries, Presets and Settings.</summary>
		public static XSettingsData<Engine.Data.PadSetting> PadSettings = new XSettingsData<Engine.Data.PadSetting>("PadSettings.xml", "User and Preset PadSettings.");

		// Used for the grid.
		public static void RefreshSettingsConnectionState(params Setting[] settings)
		{
			foreach (var item in settings)
			{
				bool isOnline;
				if (TestDeviceHelper.ProductGuid.Equals(item.ProductGuid))
				{
					isOnline = true;
				}
				else
				{
					var device = GetDevice(item.InstanceGuid);
					isOnline = device == null ? false : device.IsOnline;
				}
				if (item.IsOnline != isOnline)
					item.IsOnline = isOnline;
			}
		}

		public static Engine.Data.Setting GetSetting(Guid instanceGuid, string fileName)
		{
			return Settings.Items.FirstOrDefault(x =>
				x.InstanceGuid.Equals(instanceGuid) &&
				string.Compare(x.FileName, fileName, true) == 0
			);
		}

		/// <summary>
		/// Get settings by file name and optionally filter by mapped to XInput controller.
		/// </summary>
		public static List<Engine.Data.Setting> GetSettings(string fileName, MapTo? mapTo = null)
		{
			return Settings.Items.Where(x =>
				string.Compare(x.FileName, fileName, true) == 0 && (!mapTo.HasValue || x.MapTo == (int)mapTo.Value)
			).ToList();
		}

		public static UserDevice GetDevice(Guid instanceGuid)
		{
			return UserDevices.Items.FirstOrDefault(x =>
				x.InstanceGuid.Equals(instanceGuid));
		}

		public static PadSetting GetPadSetting(Guid padSettingChecksum)
		{
			return PadSettings.Items.FirstOrDefault(x =>
				x.PadSettingChecksum.Equals(padSettingChecksum));
		}

		public static List<UserDevice> GetDevices(string fileName, MapTo mapTo)
		{
			var settings = GetSettings(fileName);
			// Get all mapped to specific index.
			var instances = settings
				.Where(x => x.MapTo == (int)mapTo)
				.Select(x => x.InstanceGuid).ToArray();
			var devices = UserDevices.Items
				.Where(x => instances.Contains(x.InstanceGuid))
				.ToList();
			// Return available devices.
			return devices;
		}

		static object saveReadFileLock = new object();

		public static void Save(bool updateGameDatabase = false)
		{
			if (updateGameDatabase)
			{
				GameDatabaseManager.Current.SetPrograms(Programs.Items, UserGames.Items);
			}
			lock (saveReadFileLock)
			{
				Programs.Save();
				UserGames.Save();

			}
		}

		public static UserGame ProcessExecutable(string filePath)
		{
			var fi = new FileInfo(filePath);
			if (!fi.Exists) return null;
			// Check if item already exists.
			var game = UserGames.Items.FirstOrDefault(x => x.FileName.ToLower() == fi.Name.ToLower());
			if (game == null)
			{
				var scanner = new XInputMaskScanner();
				game = scanner.FromDisk(fi.FullName);
				// Load default settings.
				var program = Programs.Items.FirstOrDefault(x => x.FileName.ToLower() == game.FileName.ToLower());
				game.LoadDefault(program);
				UserGames.Items.Add(game);
			}
			else
			{
				game.FullPath = fi.FullName;
			}
			// Import INI settings.
			Current.ReadIniFile(game);
			Save();
			return game;
		}

		#region Static Version

		#region Constants

		public const string IniFileName = "x360ce.ini";
		public const string TmpFileName = "x360ce.tmp";

		public const string MappingsSection = "Mappings";
		public const string OptionsSection = "Options";

		#endregion // Constants

		#region Member Variables
		static SettingsManager _current;
		#endregion // Member Variables

		#region Public Methods
		/// <summary>
		/// Adds an entry in the control-setting map and also generates a tool-tip for the setting.
		/// </summary>
		/// <param name="sectionName">
		/// The name of the section.
		/// </param>
		/// <param name="setting">
		/// The name of the setting.
		/// </param>
		/// <param name="control">
		/// The control used to edit the setting.
		/// </param>
		/// <param name="settingsMap">
		/// The settings map to add the entry in.
		/// </param>
		static public void AddMap<T>(string sectionName, Expression<Func<T>> setting, Control control, MapTo mapTo = MapTo.None)
		{
			// Get the member expression
			var me = (MemberExpression)setting.Body;
			// Get the property
			var prop = (PropertyInfo)me.Member;
			// Get the setting name by reading the property
			var keyName = (string)prop.GetValue(null, null);
			if (string.IsNullOrEmpty(keyName))
			{
				keyName = prop.Name;
			}
			// Get the description attribute
			var descAttr = GetCustomAttribute<DescriptionAttribute>(prop);
			var desc = descAttr != null ? descAttr.Description : string.Empty;
			// Get the default value attribute
			var dvalAttr = GetCustomAttribute<DefaultValueAttribute>(prop);
			var dval = (string)(descAttr != null ? dvalAttr.Value : null);
			// Display help inside yellow header.
			// We could add settings EnableHelpTooltips=1, EnableHelpHeader=1
			control.MouseHover += control_MouseEnter;
			control.MouseLeave += control_MouseLeave;
			var item = new SettingsMapItem();
			item.Description = desc;
			item.IniSection = sectionName;
			item.IniKey = keyName;
			item.Control = control;
			item.MapTo = mapTo;
			item.PropertyName = prop.Name;
			item.DefaultValue = dval;
			// Add to the map
			Current.SettingsMap.Add(item);
		}

		static void control_MouseLeave(object sender, EventArgs e)
		{
			MainForm.Current.SetHeaderBody(MessageBoxIcon.None);
		}

		static void control_MouseEnter(object sender, EventArgs e)
		{
			var control = (Control)sender;
			var item = Current.SettingsMap.FirstOrDefault(x => x.Control == control);
			if (item != null && !string.IsNullOrEmpty(item.Description))
			{
				MainForm.Current.HelpBodyLabel.Text = item.Description;
			}
		}

		#endregion // Public Methods

		#region Public Properties
		/// <summary>
		/// Gets the SettingManager singleton instance.
		/// </summary>
		public static SettingsManager Current
		{
			get { return _current = _current ?? new SettingsManager(); }
		}
		#endregion // Public Properties
		#endregion // Static Version
		#region Instance Version

		public SettingsManager()
		{
		}

		public int saveCount = 0;
		public int loadCount = 0;

		public event EventHandler<SettingEventArgs> ConfigSaved;

		public event EventHandler<SettingEventArgs> ConfigLoaded;

		public bool IsDebugMode
		{
			get
			{
				var control = SettingsMap.FirstOrDefault(x => x.IniSection == OptionsSection && x.IniKey == SettingName.DebugMode).Control;
				return ((CheckBox)control).Checked;
			}
		}

		/// <summary>
		/// Link control with INI key. Value/Text of control will be automatically tracked and INI file updated.
		/// </summary>
		object settingsMapLock = new object();

		List<SettingsMapItem> _SettingsMap;
		public List<SettingsMapItem> SettingsMap
		{
			get
			{
				lock (settingsMapLock)
				{
					if (_SettingsMap == null)
					{
						_SettingsMap = new List<SettingsMapItem>();
					}
					return _SettingsMap;
				}
			}
		}



		//public void ReadSettings()
		//{
		//	ReadSettings(IniFileName);
		//}

		static private T GetCustomAttribute<T>(PropertyInfo prop)
		{
			if (prop == null)
			{
				return default(T);
			}

			return (T)prop.GetCustomAttributes(typeof(T), false).FirstOrDefault();
		}

		public void SetPadSetting(string padSectionName, DeviceInstance di)
		{
			var ini2 = new Ini(IniFileName);
			//ps.PadSettingChecksum = Guid.Empty;
			ini2.SetValue(padSectionName, SettingName.ProductName, di.ProductName);
			ini2.SetValue(padSectionName, SettingName.ProductGuid, di.ProductGuid.ToString());
			ini2.SetValue(padSectionName, SettingName.InstanceGuid, di.InstanceGuid.ToString());
		}

		public void SetComboBoxValue(ComboBox cbx, string text)
		{
			// Remove value from other box.
			var controls = SettingsMap.Select(x => x.Control).ToArray();
			foreach (Control control in controls)
			{
				if (
					// Control is ComboBox.
					control is ComboBox
					// controls belong to same parent.
					&& cbx.Parent == control.Parent
					// This is not same control.
					&& control != cbx
					// Text value is same.
					&& control.Text == text
					// Text value is not empty.
					&& !string.IsNullOrEmpty(text))
				{
					((ComboBox)control).SelectedIndex = -1;
					((ComboBox)control).Items.Clear();
					//SaveSettings(control);
				}
			}
			cbx.Items.Clear();
			cbx.Items.Add(text);
			cbx.SelectedIndex = 0;
		}

		#region Load Settings

		/// <summary>
		/// Read setting from INI file into windows form control.
		/// </summary>
		public void LoadSetting(Control control, string key = null, string value = null)
		{
			if (key != null && (
				key == SettingName.HookMode ||
				key.EndsWith(SettingName.GamePadType) ||
				key.EndsWith(SettingName.ForceType) ||
				key.EndsWith(SettingName.LeftMotorDirection) ||
				key.EndsWith(SettingName.RightMotorDirection) ||
				key.EndsWith(SettingName.PassThroughIndex) ||
				key.EndsWith(SettingName.CombinedIndex)
				)
			)
			{
				var cbx = (ComboBox)control;
				for (int i = 0; i < cbx.Items.Count; i++)
				{
					if (cbx.Items[i] is KeyValuePair)
					{
						var kv = (KeyValuePair)cbx.Items[i];
						if (kv.Value == value)
						{
							cbx.SelectedIndex = i;
							break;
						}
					}
					else
					{
						var kv = System.Convert.ToInt32(cbx.Items[i]);
						if (kv.ToString() == value)
						{
							cbx.SelectedIndex = i;
							break;
						}
					}
				}
			}
			// If DI menu strip attached.
			else if (control is ComboBox)
			{
				var cbx = (ComboBox)control;
				if (control.ContextMenuStrip == null)
				{
					control.Text = value;
				}
				else
				{
					var text = SettingsConverter.ToTextValue(value);
					SetComboBoxValue(cbx, text);
				}
			}
			else if (control is TextBox)
			{
				// if setting is read-only.
				if (key == SettingName.ProductName) return;
				if (key == SettingName.ProductGuid) return;
				if (key == SettingName.InstanceGuid) return;
				// Always override version.
				if (key == SettingName.Version) value = SettingName.DefaultVersion;
				control.Text = value;
			}
			else if (control is NumericUpDown)
			{
				var nud = (NumericUpDown)control;
				decimal n = 0;
				decimal.TryParse(value, out n);
				if (n < nud.Minimum) n = nud.Minimum;
				if (n > nud.Maximum) n = nud.Maximum;
				nud.Value = n;
			}
			else if (control is TrackBar)
			{
				TrackBar tc = (TrackBar)control;
				int n = 0;
				int.TryParse(value, out n);
				// convert 256  to 100%
				if (key == SettingName.AxisToDPadDeadZone || key == SettingName.AxisToDPadOffset || key == SettingName.LeftTriggerDeadZone || key == SettingName.RightTriggerDeadZone)
				{
					if (key == SettingName.AxisToDPadDeadZone && value == "") n = 256;
					n = System.Convert.ToInt32((float)n / 256F * 100F);
				}
				// Convert 500 to 100%
				else if (key == SettingName.LeftMotorPeriod || key == SettingName.RightMotorPeriod)
				{
					n = System.Convert.ToInt32((float)n / 500F * 100F);
				}
				// Convert 32767 to 100%
				else if (key == SettingName.LeftThumbDeadZoneX || key == SettingName.LeftThumbDeadZoneY || key == SettingName.RightThumbDeadZoneX || key == SettingName.RightThumbDeadZoneY)
				{
					n = System.Convert.ToInt32((float)n / ((float)Int16.MaxValue) * 100F);
				}
				if (n < tc.Minimum) n = tc.Minimum;
				if (n > tc.Maximum) n = tc.Maximum;
				tc.Value = n;
			}
			else if (control is CheckBox)
			{
				CheckBox tc = (CheckBox)control;
				int n = 0;
				int.TryParse(value, out n);
				tc.Checked = n != 0;
			}
		}

		#endregion

		#region Save Settings

		public string GetSettingValue(Control control)
		{
			var item = SettingsMap.First(x => x.Control == control);
			var path = item.IniPath;
			var section = path.Split('\\')[0];
			string key = path.Split('\\')[1];
			var padIndex = SettingName.GetPadIndex(path);
			string v = string.Empty;
			if (key == SettingName.HookMode ||
				key.EndsWith(SettingName.GamePadType) ||
				key.EndsWith(SettingName.ForceType) ||
				key.EndsWith(SettingName.LeftMotorDirection) ||
				key.EndsWith(SettingName.RightMotorDirection) ||
				key.EndsWith(SettingName.PassThroughIndex) ||
				key.EndsWith(SettingName.CombinedIndex))
			{
				var v1 = ((ComboBox)control).SelectedItem;
				if (v1 == null) { v = "0"; }
				else if (v1 is KeyValuePair) { v = ((KeyValuePair)v1).Value; }
				else { v = System.Convert.ToInt32(v1).ToString(); }
			}
			// If DI menu strip attached.
			else if (control is ComboBox)
			{
				var cbx = (ComboBox)control;
				if (control.ContextMenuStrip == null)
				{
					v = control.Text;
				}
				else
				{
					v = SettingsConverter.ToIniValue(control.Text);
					// make sure that disabled button value is "0".
					if (SettingName.IsButton(key) && string.IsNullOrEmpty(v)) v = "0";
				}
				// Save to XML.
				if (key == SettingName.InternetDatabaseUrl)
				{
					Options.InternetDatabaseUrl = v;
				}
			}
			else if (control is TextBox)
			{
				// if setting is read-only.
				if (key == SettingName.InstanceGuid || key == SettingName.ProductGuid)
				{
					v = string.IsNullOrEmpty(control.Text) ? Guid.Empty.ToString("D") : control.Text;
				}
				else v = control.Text;
			}
			else if (control is NumericUpDown)
			{
				NumericUpDown nud = (NumericUpDown)control;
				v = nud.Value.ToString();
			}
			else if (control is TrackBar)
			{
				TrackBar tc = (TrackBar)control;
				// convert 100%  to 256
				if (key == SettingName.AxisToDPadDeadZone || key == SettingName.AxisToDPadOffset || key == SettingName.LeftTriggerDeadZone || key == SettingName.RightTriggerDeadZone)
				{
					v = System.Convert.ToInt32((float)tc.Value / 100F * 256F).ToString();
				}
				// convert 100%  to 500
				else if (key == SettingName.LeftMotorPeriod || key == SettingName.RightMotorPeriod)
				{
					v = System.Convert.ToInt32((float)tc.Value / 100F * 500F).ToString();
				}
				// Convert 100% to 32767
				else if (key == SettingName.LeftThumbDeadZoneX || key == SettingName.LeftThumbDeadZoneY || key == SettingName.RightThumbDeadZoneX || key == SettingName.RightThumbDeadZoneY)
				{
					v = System.Convert.ToInt32((float)tc.Value / 100F * ((float)Int16.MaxValue)).ToString();
				}
				else v = tc.Value.ToString();
			}
			else if (control is CheckBox)
			{
				CheckBox tc = (CheckBox)control;
				v = tc.Checked ? "1" : "0";
			}
			else if (control is DataGridView)
			{
				var grid = (DataGridView)control;
				if (grid.Enabled)
				{
					var data = grid.Rows.Cast<DataGridViewRow>().Where(x => x.Visible).Select(x => x.DataBoundItem as Setting)
						// Make sure that only enabled controllers are added.
						.Where(x => x != null && x.IsEnabled).ToArray();
					var instances = data.Select(x => GetInstanceSection(x.InstanceGuid)).ToArray();
					// Separate devices with comma. x360ce.dll must combine devices separated by comma.
					v = string.Join(",", instances);
				}
				else
				{
					v = "AUTO";
				}
			}
			return v;
		}

		#endregion

		static Guid GetInstanceGuid(MapTo mapTo)
		{
			var guidString = Current.SettingsMap
				.First(x => x.MapTo == mapTo && x.IniKey == SettingName.InstanceGuid).Control.Text;
			// If instanceGuid value is not a GUID then exit.
			if (!EngineHelper.IsGuid(guidString)) return Guid.Empty;
			Guid ig = new Guid(guidString);
			return ig;
		}

		static string GetInstanceSection(MapTo mapTo)
		{
			var ig = GetInstanceGuid(mapTo);
			// If InstanceGuid value is empty then exit.
			if (ig.Equals(Guid.Empty)) return null;
			// Save settings to unique Instance section.
			return Current.GetInstanceSection(ig);
		}

		public string GetInstanceSection(Guid instanceGuid)
		{
			return string.Format("IG_{0:N}", instanceGuid).ToUpper();
		}

		public string GetProductSection(Guid productGuid)
		{
			return string.Format("PG_{0:N}", productGuid).ToUpper();
		}


		public bool ContainsProductSection(Guid productGuid, string iniFileName, out string sectionName)
		{
			var ini2 = new Ini(iniFileName);
			var section = GetProductSection(productGuid);
			var contains = !string.IsNullOrEmpty(ini2.GetValue(section, "ProductGUID"));
			sectionName = contains ? section : null;
			return contains;
		}

		public bool ContainsInstanceSection(Guid instanceGuid, string iniFileName, out string sectionName)
		{
			var ini2 = new Ini(iniFileName);
			var section = GetInstanceSection(instanceGuid);
			var contains = !string.IsNullOrEmpty(ini2.GetValue(section, "InstanceGUID"));
			sectionName = contains ? section : null;
			return contains;
		}

		public bool ContainsInstanceSectionOld(Guid instanceGuid, string iniFileName, out string sectionName)
		{
			var ini2 = new Ini(iniFileName);
			for (int i = 1; i <= 4; i++)
			{
				var section = string.Format("PAD{0}", i);
				var v = ini2.GetValue(section, "InstanceGUID");
				if (string.IsNullOrEmpty(v)) continue;
				if (v.ToLower() == instanceGuid.ToString("D").ToLower())
				{
					sectionName = section;
					return true;
				}
			}
			sectionName = null;
			return false;
		}

		#endregion // Instance Version

		public void FillSearchParameterWithInstances(List<SearchParameter> sp)
		{
			// Select user devices as parameters to search.
			var userDevices = Settings.Items
				.Select(x => x.InstanceGuid).Distinct()
				// Do not add empty records.
				.Where(x => x != Guid.Empty)
				.Select(x => new SearchParameter() { InstanceGuid = x })
				.ToArray();
			sp.AddRange(userDevices);
		}

		public void FillSearchParameterWithFiles(List<SearchParameter> sp)
		{
			// Select user games as parameters to search.
			var settings = Settings.Items.ToArray();
			foreach (var setting in settings)
			{
				var fileName = Path.GetFileName(setting.FileName);
				// Do not add empty records.
				if (string.IsNullOrEmpty(fileName))
					continue;
				var fileTitle = EngineHelper.FixName(setting.ProductName, setting.FileName);
				// If search doesn't contain game then...
				if (!sp.Any(x => x.FileName == fileName && x.FileProductName == fileTitle))
				{
					// Add it to search parameters.
					var p = new SearchParameter();
					p.FileName = fileName;
					p.FileProductName = fileTitle;
					sp.Add(p);
				}
			}
		}
	}
}
