﻿using MyAndromeda.Data.DataWarehouse;
using MyAndromeda.Data.DataWarehouse.Models;
using MyAndromeda.Data.DataWarehouse.Services.Orders;
using MyAndromeda.Logging;
using MyAndromeda.Services.Bringg.IncomingWebHooks;
using MyAndromeda.Services.Bringg.Models;
using MyAndromeda.Services.Bringg.Outgoing;
using MyAndromeda.Services.Bringg.Services;
using MyAndromeda.Services.WebHooks;
using MyAndromeda.Services.WebHooks.Models;
using MyAndromeda.WebApiClient;
using MyAndromedaDataAccessEntityFramework.DataAccess.Sites;
using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using System.Monads;
using Newtonsoft.Json;

namespace MyAndromeda.Web.Controllers.Api.Bringg
{
    public class BringApiController : ApiController
    {
        private readonly IWebApiClientContext webApiClientContext;
        private readonly IMyAndromedaLogger logger;
        private readonly IOrderHeaderDataService orderHeaderService;
        private readonly IStoreDataService storeDataService;
        private readonly WebhookEndpointManger webhookEndpointManger;
        private readonly IBringgSettingsService settingsService;
        private readonly IBringgGetTaskService getTaskService;


        public BringApiController(IWebApiClientContext webApiClientContext, IMyAndromedaLogger logger, IOrderHeaderDataService orderHeaderService, IStoreDataService storeDataService, WebhookEndpointManger webhookEndpointManger, IBringgSettingsService settingsService, IBringgGetTaskService getTaskService) 
        {
            this.getTaskService = getTaskService;
            this.settingsService = settingsService;
            this.webhookEndpointManger = webhookEndpointManger;
            this.storeDataService = storeDataService;
            this.orderHeaderService = orderHeaderService;
            this.logger = logger;
            this.webApiClientContext = webApiClientContext;
        }

        private async Task<OrderHeader> GetOrderHeaderAsync(int bringgTaskId)
        {
            try
            {
                if (bringgTaskId == 0)
                {
                    string message = "The Bringg task id is missing!";
                    this.logger.Error(message);
                }

                OrderHeader[] order = await this.orderHeaderService.OrderHeaders
                    .Where(e => e.BringgTaskId == bringgTaskId)
                    .OrderByDescending(e=> e.TimeStamp)
                    .ToArrayAsync();
                //.SingleOrDefaultAsync();

                if (order.Length == 0)
                {
                    this.logger.Error("There is no order with the bringg id : " + bringgTaskId);
                    throw new NullReferenceException("order");
                }
                else if (order.Length > 1)
                {
                    this.logger.Error("There are too many orders with the Bringg id: {0}", bringgTaskId);
                }
                
                return order.FirstOrDefault();
            }
            catch (Exception ex)
            {
                this.logger.Error(ex);
            }

            return null;
        }

        private async Task<StoreDetails> GetStoreIdsAsync(OrderHeader model) 
        {
            try
            {
                var basicQuery = this.storeDataService.Table
                     .Where(e => e.ExternalId == model.ExternalSiteID);

                //1100000000 Rameses - special number
                if (model.ApplicationID != 1100000000)
                {
                    basicQuery = basicQuery.Where(e => e.ACSApplicationSites.Any(s => s.ACSApplicationId == model.ApplicationID)); 
                }

                //projection query 
                IQueryable<StoreDetails> query = basicQuery
                    .Select(e => new StoreDetails { ExternalSiteId = e.ExternalId, AndromedaSiteId = e.AndromedaSiteId, AndroAdminStoreId = e.Id });//.ToArrayAsync();
                
                //projection result
                StoreDetails[] queryResult = await query.ToArrayAsync();

                if (queryResult.Length > 1)
                {
                    this.logger.Error("There were more than one store returned by ACSApplicationId: {0} and ExternalSiteID: {1}", model.ApplicationID, model.ExternalSiteID);
                }
                if (queryResult.Length == 0)
                {
                    this.logger.Error("There were no stores returned by ACSApplicationId: {0} and ExternalSiteID: {1}", model.ApplicationID, model.ExternalSiteID);
                }

                StoreDetails store = queryResult.FirstOrDefault();

                return store;
            }
            catch (Exception ex)
            {
                this.logger.Error(ex);
            }

            return null;
        }

        private async Task<OutgoingWebHookOrderStatusChange> GetOrderStatusModel(int taskId, BringWebhook model, UsefulOrderStatus orderStatus) 
        {
            var order = await this.GetOrderHeaderAsync(taskId);
            var store = await this.GetStoreIdsAsync(order);

            var sendModel = new OutgoingWebHookOrderStatusChange()
            {
                AndromedaSiteId = store.AndromedaSiteId,
                ExternalSiteId = store.ExternalSiteId,
                ExternalOrderId = order.ExternalOrderRef,
                //InternalOrderId = order.Id,
                Source = "Bringg -> ACS",
                AcsApplicationId = order.ApplicationID,
                Status = (int)orderStatus,
                StatusDescription = orderStatus.Describe(),
            };

            return sendModel;
        }

        private async Task<OutgoingWebHookBringg> GetOrderHeader(int taskId, BringWebhook model) 
        {
            logger.Debug("Creating the model for the Bringg webhook based on the incoming.");

            OrderHeader order = await this.GetOrderHeaderAsync(taskId);
            StoreDetails store = await this.GetStoreIdsAsync(order);

            //check nulls 
            if (model.IsNull()) 
            {
                string message = "Where is the Bringg Message?";
                logger.Error(message);
                throw new ArgumentException("model", new Exception(message));
            }
            if (order.IsNull()) 
            {
                string message = "Order could not be matched. Bringg will blow up."; 
                logger.Error(message);
                throw new ArgumentException("order", new Exception(message));
            }

            if (store.IsNull()) 
            {
                string message = "The store could not be matched. Bringg will blow up.";
                logger.Error(message);
                throw new Exception(message);
            }

            //check values ... if they fail ... throw a exception. 

            store.AndromedaSiteId.Check(e => e > 0, e => new ArgumentException("AndroAdminStoreId is missing"));
            store.ExternalSiteId.Check(e => !string.IsNullOrWhiteSpace(e), e => new ArgumentException("external site id is missing"));
            order.ID.Check(e => e != default(Guid), e => new ArgumentException("internal order id is missing"));
            order.ExternalOrderRef.Check(e => !string.IsNullOrWhiteSpace(e), e => new ArgumentException("External Order Ref is missing"));
            taskId.Check(e => e > 0, e => new ArgumentException("BringgTaskId"));
            //model.Status.Check(e => !string.IsNullOrWhiteSpace(model.Status), e => new ArgumentException("Status"));
            
            //nothing can be a null object except order.ID :D why is it blowing up :S 
            var sendModel = new OutgoingWebHookBringg()
            {
                AndromedaSiteId = store.AndromedaSiteId,
                ExternalSiteId = store.ExternalSiteId,
                AndromedaOrderId = order.ID.ToString(),
                ExternalId = order.ExternalOrderRef,
                //Id = model.Id,
                Id = taskId,
                Source = "Bringg -> ACS",
                Status = model.Status,
                AndromedaOrderStatusId = order.Status,
                UserId = model.UserId
            };

            return sendModel;
        }

        [HttpGet]
        [Route("bringg/get/{taskId}")]
        public async Task<BringgTaskModel> Get([FromUri]int taskId) 
        {
            var order = await this.GetOrderHeaderAsync(taskId);
            var store = await this.GetStoreIdsAsync(order);

            var settings = this.settingsService.Get(store.AndroAdminStoreId);
            var task = await this.getTaskService.Get(settings, taskId);

            return task;
        }

        [HttpPost]
        [Route("bringg/notifyTaskCreatedUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskCreated([FromUri]int taskId
            //, [FromBody]BringWebhook model
            ) 
        {
            this.logger.Debug("notifyTaskCreatedUrl: " + taskId);
            
            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);
                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): Task created error");
                this.logger.Error(ex);
            }
            
            return Ok();
        }

        [HttpPost, HttpPut]
        [Route("bringg/notifyTaskAssignedUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskAssigned([FromUri]int taskId
            //, [FromBody]BringWebhook model
            )
        {
            this.logger.Debug("bringg - notifyTaskAssignedUrl: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);
                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyTaskAssigned error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        [HttpPost, HttpPut]
        [Route("bringg/notifyTaskOnTheWayUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskOnTheWay([FromUri]int taskId
            //, [FromBody]BringWebhook model
            )
        {
            this.logger.Debug("bringg - notifyTaskOnTheWayUrl: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
                await this.UpdateOrderSatatus(taskId, model, UsefulOrderStatus.OrderIsOutForDelivery);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyTaskOnTheWay error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        [HttpPost]
        [Route("bringg/notifyTaskCheckedInUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskCheckedIn([FromUri]int taskId 
            //, [FromBody]BringWebhook model
            )
        {
            this.logger.Debug("bringg - notifyTaskCheckedInUrl: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyTaskCheckedIn error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        [HttpPost]
        [Route("bringg/notifyArrivedOnLocationUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskArrived([FromUri]int taskId
            //,[FromBody]BringWebhook model
            )
        {
            this.logger.Debug("bringg - NotifyTaskArrived: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyTaskArrived error");
                this.logger.Error(ex);
            }
            
            return Ok();
        }

        [HttpPost]
        [Route("bringg/notifyTaskDoneUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskDone([FromUri]int taskId
            //, [FromBody]BringWebhook model
            )
        {
            this.logger.Debug("bringg - NotifyTaskDone: " + taskId);
            
            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
                await this.UpdateOrderSatatus(taskId, model, UsefulOrderStatus.OrderHasBeenCompleted);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyTaskDone error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        [HttpPost] 
        [Route("bringg/notifyTaskAcceptedUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskAccepted([FromUri]int taskId
            //, [FromBody]BringWebhook model
            )
        {
            this.logger.Debug("bringg - NotifyTaskAccepted: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyTaskAccepted error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        [HttpPost]
        [Route("bringg/notifyTaskCancelledUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskCancelled([FromUri]int taskId 
            //[FromBody]BringWebhook model
            )
        {
            this.logger.Debug("bringg - NotifyTaskCancelled: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from web-hook): NotifyTaskCancelled error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        [HttpPost]
        [Route("bringg/notifyTaskRejectedUrl/{taskId}")]
        public async Task<OkResult> NotifyTaskRejected([FromUri]int taskId) 
            //[FromBody]BringWebhook model)
        {
            this.logger.Debug("bringg - NotifyTaskRejected: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyTaskRejected error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        [HttpPost]
        [Route("bringg/notifyLateUrl/{taskId}")]
        public async Task<OkResult> NotifyLate([FromUri]int taskId
            //, [FromBody]BringWebhook model
            ) 
        {
            this.logger.Debug("bringg - NotifyLate: " + taskId);

            try
            {
                string body = await this.Request.Content.ReadAsStringAsync();
                var model = JsonConvert.DeserializeObject<BringWebhook>(body);

                var sendModel = await this.GetOrderHeader(taskId, model);

                await this.CreateBringgWebhookRequest(sendModel);
            }
            catch (Exception ex)
            {
                this.logger.Error("Bringg (from webhook): NotifyLate error");
                this.logger.Error(ex);
            }

            return Ok();
        }

        //[Route("bringg/notifyETAChangedUrl")]
        //public async Task<OkResult> NotifyEtaChanged([FromUri]string taskId, [FromBody]BringgWebhookWayPointUpdate model)
        //{
        //    var sendModel = await this.GetEtaModel(model);

        //    //await this.CreateRequest(sendModel);

        //    return Ok();
        //}

        private async Task<bool> UpdateOrderSatatus(int taskId, BringWebhook model, UsefulOrderStatus orderStatus) 
        {
            bool result = false;

            var sendModel = this.GetOrderStatusModel(taskId, model, orderStatus);

            try
            {
                this.logger.Debug("Calling: " + webApiClientContext.BaseAddress + this.webhookEndpointManger.OrderStatus);
                using (var client = new HttpClient())
                {
                    // New code:
                    client.BaseAddress = new Uri(webApiClientContext.BaseAddress);
                    //client.DefaultRequestHeaders.Accept.Clear();
                    //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage response = await
                        client.PostAsJsonAsync(this.webhookEndpointManger.OrderStatus, sendModel);

                    if (!response.IsSuccessStatusCode)
                    {
                        string message = string.Format("Problem calling: {0}", this.webhookEndpointManger.OrderStatus);
                        string responseMessage = await response.Content.ReadAsStringAsync();
                        throw new WebException(message, new Exception(responseMessage));
                    }

                    result = true;
                }
            }
            catch (Exception e)
            {
                this.logger.Error(e);
            }

            return result;
        }

        private async Task<bool> CreateBringgWebhookRequest(OutgoingWebHookBringg bringWebook)
        {
            bool result = false;

            try
            {
                this.logger.Debug("Calling: " + webApiClientContext.BaseAddress + this.webhookEndpointManger.BringgEndpoint);
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(webApiClientContext.BaseAddress);

                    HttpResponseMessage response = await client.PostAsJsonAsync(this.webhookEndpointManger.BringgEndpoint, bringWebook);

                    if (!response.IsSuccessStatusCode)
                    {
                        string message = string.Format("Could not call : {0}", this.webhookEndpointManger.BringgEndpoint);
                        string responseMessage = await response.Content.ReadAsStringAsync();

                        throw new WebException(message, new Exception(responseMessage));
                    }

                    result = true; 
                }
            }
            catch (Exception e)
            {
                this.logger.Error(e);
            }

            return result;
        }

    }

    public class StoreDetails
    {
        public string ExternalSiteId { get; set; }
        public int AndromedaSiteId { get; set; }
        public int AndroAdminStoreId { get; set; }
    }
}