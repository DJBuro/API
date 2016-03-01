﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using Kendo.Mvc.UI;
using MyAndromeda.CloudSynchronization.Services;
using MyAndromeda.Data.Model;
using MyAndromeda.Framework.Contexts;
using MyAndromeda.Logging;
using MyAndromeda.Web.Controllers.Api.Store.Models;
using MyAndromedaDataAccessEntityFramework.DataAccess.Sites;
using Newtonsoft.Json;

namespace MyAndromeda.Web.Controllers.Api.Store
{
    [RoutePrefix("api/chain/{chainId}/store")]
    public class StoreController : ApiController
    {
        private readonly IMyAndromedaLogger logger;

        private readonly ICurrentChain currentChain;
        private readonly ICurrentSite currentStore;

        private readonly IStoreDataService storeDataService;
        private readonly MyAndromeda.Data.Model.AndroAdmin.AndroAdminDbContext androAdminDbContext;
        private readonly DbSet<MyAndromeda.Data.Model.AndroAdmin.StoreOccasionTime> storeOccasionTimes;
        private readonly DbSet<MyAndromeda.Data.Model.AndroAdmin.Store> stores;
        private readonly ISynchronizationTaskService acsSynchronizationTaskService;

        public StoreController(IStoreDataService storeDataService, MyAndromeda.Data.Model.AndroAdmin.AndroAdminDbContext androAdminDbContext, ICurrentSite currentStore, ICurrentChain currentChain, IMyAndromedaLogger logger, ISynchronizationTaskService acsSynchronizationTaskService)
        {
            this.acsSynchronizationTaskService = acsSynchronizationTaskService;
            this.logger = logger;
            this.currentChain = currentChain;
            this.currentStore = currentStore;
            this.androAdminDbContext = androAdminDbContext;
            this.storeOccasionTimes = androAdminDbContext.Set<MyAndromeda.Data.Model.AndroAdmin.StoreOccasionTime>();
            this.stores = androAdminDbContext.Set<MyAndromeda.Data.Model.AndroAdmin.Store>();

            this.storeDataService = storeDataService;
        }

        [HttpGet]
        public IEnumerable<object> Get() 
        {
            return this.storeDataService.List()
                .Select(e => new { 
                e.ExternalId,
                e.AndromedaSiteId,
                e.ClientSiteName
            })
            .OrderBy(e=> e.ClientSiteName);
        }

        [HttpPost]
        [Route("{andromedaSiteId}/Occasions")]
        public async Task<DataSourceResult> ListOcassionTimes(int andromedaSiteId) 
        {
            string content = await this.Request.Content.ReadAsStringAsync();

            DataSourceRequest request = JsonConvert.DeserializeObject<DataSourceRequest>(content);

            var occasions = await this.storeOccasionTimes
                .Where(e => e.Store.AndromedaSiteId == andromedaSiteId)
                .Where(e=> !e.Deleted)
                .ToArrayAsync();


            await this.androAdminDbContext.SaveChangesAsync();

            return new DataSourceResult()
            {
                Total = occasions.Length,
                Data = occasions.Select(e => e.ToViewModel())
            };

            //return  
        }

        [HttpPost]
        [Route("{andromedaSiteId}/update-occasion")]
        public async Task<DataSourceResult> Update(int andromedaSiteId) 
        {
            string content = await this.Request.Content.ReadAsStringAsync();

            StoreOccasionTimeModel model = JsonConvert.DeserializeObject<StoreOccasionTimeModel>(content);

            var storeEntity = await this.stores.FirstOrDefaultAsync(e => e.AndromedaSiteId == andromedaSiteId);
            var occasionEntity = await this.storeOccasionTimes.FirstOrDefaultAsync(e => e.Id == model.Id);

            if (occasionEntity == null) 
            {
                occasionEntity = model.CreateEntiy(storeEntity);
                
                this.storeOccasionTimes.Add(occasionEntity);
            }

            occasionEntity.DataVersion = this.androAdminDbContext.GetNextDataVersionForEntity();
            occasionEntity.Update(model);

            model = occasionEntity.ToViewModel();

            try
            {
                await this.androAdminDbContext.SaveChangesAsync();


                this.acsSynchronizationTaskService.CreateTask(new MyAndromeda.Data.Model.MyAndromeda.CloudSynchronizationTask() { 
                    ChainId = this.currentChain.Chain.Id,
                    Description = "Sync Task for update hours on occasions", 
                    StoreId = this.currentStore.Store.Id,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                this.logger.Error(ex);

                throw;
            }
            

            return new DataSourceResult()
            {
                Total = 1, 
                Data = new[] { model }
            };
        }

        [HttpPost]
        [Route("{andromedaSiteId}/delete-occasion")]
        public async Task<DataSourceResult> Destroy(int andromedaSiteId)
        {
            string content = await this.Request.Content.ReadAsStringAsync();

            StoreOccasionTimeModel model = JsonConvert.DeserializeObject<StoreOccasionTimeModel>(content);

            var entity = await this.storeOccasionTimes.FirstOrDefaultAsync(e => e.Id == model.Id);

            if (entity != null) 
            {
                entity.Deleted = true;
                entity.DataVersion = this.androAdminDbContext.GetNextDataVersionForEntity();
                //this.storeOccasionTimes.Remove(entity);
            }

            try
            {
                await this.androAdminDbContext.SaveChangesAsync();
                this.acsSynchronizationTaskService.CreateTask(new MyAndromeda.Data.Model.MyAndromeda.CloudSynchronizationTask() { 
                    ChainId = this.currentChain.Chain.Id,
                    Description = "Sync Task for update hours on occasions", 
                    StoreId = this.currentStore.Store.Id,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                this.logger.Error(ex);
                
                throw;
            }
           

            return new DataSourceResult()
            {
                Total = 1,
                Data = new[] { model }
            };
        }
    }
}
