﻿//----------------------------------------------------------------------------------------------
//    Copyright 2015 Microsoft Corporation
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.WindowsAzure.MediaServices.Client;
using Excel = Microsoft.Office.Interop.Excel;
using Microsoft.WindowsAzure.MediaServices.Client.DynamicEncryption;
using System.Reflection;
using System.IO;

namespace AMSExplorer
{
    public partial class UploadBulk : Form
    {
        private BindingList<BulkAssetFile> assetFiles = new BindingList<BulkAssetFile>();
        private CloudMediaContext _context;

        public string AssetName
        {
            get
            {
                return textBoxAssetName.Text;
            }
        }

        public string[] AssetFiles
        {
            get
            {
                return assetFiles.Select(a => a.FileName).ToArray();
            }
        }

        public DownloadToFolderOption FolderOption
        {
            get
            {
                DownloadToFolderOption option = DownloadToFolderOption.DoNotCreateSubfolder;
                if (checkBoxCreateSubfolder.Checked)
                {
                    option = radioButtonAssetName.Checked ? DownloadToFolderOption.SubfolderAssetName : DownloadToFolderOption.SubfolderAssetId;
                }
                return option;
            }

        }



        public UploadBulk(CloudMediaContext context)
        {
            InitializeComponent();
            this.Icon = Bitmaps.Azure_Explorer_ico;
            dataGridAssetFiles.DataSource = assetFiles;
            _context = context;
        }

        private void UploadBulk_Load(object sender, EventArgs e)
        {

        }


        private void buttonCancel_Click(object sender, EventArgs e)
        {
        }

        private void buttonAddFiles_Click(object sender, EventArgs e)
        {
            assetFiles.AddNew();
        }

        private void buttonDelFiles_Click(object sender, EventArgs e)
        {
            if (dataGridAssetFiles.SelectedRows.Count == 1)
            {
                assetFiles.RemoveAt(dataGridAssetFiles.SelectedRows[0].Index);
            }
        }


        class BulkAssetFile
        {
            string _fileName;
            public string FileName { get { return _fileName; } set { _fileName = value; } }

            public BulkAssetFile()
            {
                _fileName = string.Empty;
            }
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {

        }
    }


}
