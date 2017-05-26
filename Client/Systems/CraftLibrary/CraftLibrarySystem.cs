﻿using LunaClient.Base;
using LunaClient.Utilities;
using LunaCommon.Enums;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LunaClient.Systems.CraftLibrary
{
    public class CraftLibrarySystem :
        MessageSystem<CraftLibrarySystem, CraftLibraryMessageSender, CraftLibraryMessageHandler>
    {
        private CraftLibraryEvents CraftLibraryEventHandler { get; } = new CraftLibraryEvents();

        #region Fields

        //Public
        public Queue<CraftChangeEntry> CraftAddQueue { get; } = new Queue<CraftChangeEntry>();
        public Queue<CraftChangeEntry> CraftDeleteQueue { get; } = new Queue<CraftChangeEntry>();
        public Queue<CraftResponseEntry> CraftResponseQueue { get; } = new Queue<CraftResponseEntry>();

        public string SelectedPlayer { get; set; }
        public List<string> PlayersWithCrafts { get; } = new List<string>();
        //Player -> Craft type -> Craft Name
        public Dictionary<string, Dictionary<CraftType, List<string>>> PlayerList { get; } =
            new Dictionary<string, Dictionary<CraftType, List<string>>>();

        //Craft type -> Craft Name
        public Dictionary<CraftType, List<string>> UploadList { get; } = new Dictionary<CraftType, List<string>>();

        #region Paths

        private static string SavePath { get; } = CommonUtil.CombinePaths(Client.KspPath, "saves", "LunaMultiPlayer");
        public string VabPath { get; } = CommonUtil.CombinePaths(SavePath, "Ships", "VAB");
        public string SphPath { get; } = CommonUtil.CombinePaths(SavePath, "Ships", "SPH");
        public string SubassemblyPath { get; } = CommonUtil.CombinePaths(SavePath, "Subassemblies");

        #endregion

        //upload event
        public CraftType UploadCraftType { get; set; }
        public string UploadCraftName { get; set; }
        //download event
        public CraftType DownloadCraftType { get; set; }
        public string DownloadCraftName { get; set; }
        //delete event
        public CraftType DeleteCraftType { get; set; }
        public string DeleteCraftName { get; set; }

        #endregion

        #region Base overrides

        protected override void OnEnabled()
        {
            base.OnEnabled();
            BuildUploadList();
            SetupRoutine(new RoutineDefinition(0, RoutineExecution.Update, HandleCraftLibraryEvents));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            CraftAddQueue.Clear();
            CraftDeleteQueue.Clear();
            CraftResponseQueue.Clear();
            PlayersWithCrafts.Clear();
            PlayerList.Clear();
            UploadList.Clear();
            SelectedPlayer = "";
            UploadCraftType = CraftType.Vab;
            UploadCraftName = "";
            DownloadCraftType = CraftType.Vab;
            DownloadCraftName = "";
            DeleteCraftType = CraftType.Vab;
            DeleteCraftName = "";
        }

        #endregion

        #region Update methods

        private void HandleCraftLibraryEvents()
        {
            if (Enabled && MainSystem.Singleton.GameRunning)
            {
                CraftLibraryEventHandler.HandleCraftLibraryEvents();
            }
        }

        #endregion

        #region Public methods

        public void BuildUploadList()
        {
            UploadList.Clear();
            if (Directory.Exists(VabPath))
                UploadList.Add(CraftType.Vab,
                    Directory.GetFiles(VabPath).Select(Path.GetFileNameWithoutExtension).ToList());
            if (Directory.Exists(SphPath))
                UploadList.Add(CraftType.Sph,
                    Directory.GetFiles(SphPath).Select(Path.GetFileNameWithoutExtension).ToList());
            if (Directory.Exists(VabPath))
                UploadList.Add(CraftType.Subassembly,
                    Directory.GetFiles(SubassemblyPath).Select(Path.GetFileNameWithoutExtension).ToList());
        }

        public void QueueCraftAdd(CraftChangeEntry entry)
        {
            CraftAddQueue.Enqueue(entry);
        }

        public void QueueCraftDelete(CraftChangeEntry entry)
        {
            CraftDeleteQueue.Enqueue(entry);
        }

        public void QueueCraftResponse(CraftResponseEntry entry)
        {
            CraftResponseQueue.Enqueue(entry);
        }

        #endregion
    }
}