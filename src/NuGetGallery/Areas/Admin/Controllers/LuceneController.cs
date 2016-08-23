﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using System.Web.Mvc;
using NuGetGallery.Areas.Admin.Models;
using NuGetGallery.Configuration;
using NuGetGallery.Diagnostics;
using NuGetGallery.Infrastructure.Lucene;

namespace NuGetGallery.Areas.Admin.Controllers
{
    public partial class LuceneController : AdminControllerBase
    {
        protected IIndexingService IndexingService { get; set; }
        protected IGalleryConfigurationService ConfigService { get; set; }

        protected LuceneController() { }
        public LuceneController(IIndexingService indexingService, IGalleryConfigurationService configService)
        {
            IndexingService = indexingService;
            ConfigService = configService;
        }

        //
        // GET: /Admin/Lucene/

        public virtual async Task<ActionResult> Index()
        {
            return View("Index", await GetLuceneInfo());
        }

        private async Task<LuceneInfoModel> GetLuceneInfo()
        {
            var model = new LuceneInfoModel
            {
                Directory = IndexingService.IndexPath,
                IsLocal = IndexingService.IsLocal,
                Location = (await ConfigService.GetCurrent()).LuceneIndexLocation
            };

            try
            {
                model.LastUpdated = await IndexingService.GetLastWriteTime();
                model.DocumentCount = await IndexingService.GetDocumentCount();
                model.IndexSize = await IndexingService.GetIndexSizeInBytes();
                model.QueryStats = PerfCounters.GetStats(ExternalSearchService.SearchRoundtripTimePerfCounter);
            }
            catch (FileNotFoundException)
            {
                model.LastUpdated = null;
            }
            return model;
        }

        [HttpPost]
        public virtual Task<ActionResult> Rebuild()
        {
            IndexingService.UpdateIndex(forceRefresh: true);
            return Index();
        }

    }
}
