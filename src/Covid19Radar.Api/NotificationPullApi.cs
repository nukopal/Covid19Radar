using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Covid19Radar.DataStore;
using Covid19Radar.Models;
using Microsoft.Azure.Cosmos;
using Covid19Radar.Services;
using Covid19Radar.DataAccess;

#nullable enable

namespace Covid19Radar
{
    public class NotificationPullApi
    {
        private readonly IUserRepository UserRepository;
        private readonly INotificationService Notification;
        private readonly IValidationUserService Validation;
        private readonly ILogger<NotificationPullApi> Logger;

        public NotificationPullApi(
            IUserRepository userRepository,
            INotificationService notification,
            IValidationUserService validation,
            ILogger<NotificationPullApi> logger)
        {
            UserRepository = userRepository;
            Notification = notification;
            Validation = validation;
            Logger = logger;
        }

        [FunctionName(nameof(NotificationPullApi))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get",
                         Route = "Notification/Pull/{UserUuid}/{UserMajor}/{UserMinor}/{LastNotificationTime:datetime}")]
            HttpRequest req,
            string userUuid,
            string userMajor,
            string userMinor,
            DateTime lastNotificationTime)
        {
            Logger.LogInformation($"{nameof(NotificationPullApi)} processed a request.");

            switch (req.Method)
            {
                case "GET":
                    return await Get(req, new NotificationPullParameter()
                    {
                        UserUuid = userUuid,
                        UserMajor = userMajor,
                        UserMinor = userMinor,
                        LastNotificationTime = lastNotificationTime
                    });
            }
            AddBadRequest(req);
            return new BadRequestObjectResult("Not Supported");
        }

        private async Task<IActionResult> Get(HttpRequest req, NotificationPullParameter param)
        {
            // validation
            var validationResult = await Validation.ValidateAsync(req, param);
            if (!validationResult.IsValid)
            {
                AddBadRequest(req);
                return validationResult.ErrorActionResult;
            }

            var queryResult = await Query(req, param);
            if (queryResult != null)
            {
                return queryResult;
            }

            // Query to Notification Service.
            return GetMessages(param);
        }

        [FunctionName(nameof(NotificationPullApi) + "Post")]
        public async Task<IActionResult> RunPost(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "Notification/Pull")] HttpRequest req)
        {
            Logger.LogInformation($"{nameof(NotificationPullApi)} processed a request.");

            switch (req.Method)
            {
                case "POST":
                    return await Post(req);
            }
            AddBadRequest(req);
            return new BadRequestObjectResult("Not Supported");
        }

        private async Task<IActionResult> Post(HttpRequest req)
        {
            // convert Postdata to BeaconDataModel
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var param = JsonConvert.DeserializeObject<NotificationPullParameter>(requestBody);

            // validation
            var validationResult = await Validation.ValidateAsync(req, param);
            if (!validationResult.IsValid)
            {
                AddBadRequest(req);
                return validationResult.ErrorActionResult;
            }

            var queryResult = await Query(req, param);
            if (queryResult != null)
            {
                return queryResult;
            }

            // Query to Notification Service.
            return GetMessages(param);
        }

        private async Task<IActionResult?> Query(HttpRequest req, IUser user)
        {
            try
            {
                if (await UserRepository.Exists(user.GetId()))
                {
                    return null;
                }
            }
            catch (CosmosException ex)
            {
                // 429-TooManyRequests
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return new StatusCodeResult(503);
                }
            }
            AddBadRequest(req);
            return new BadRequestObjectResult("");
        }

        private IActionResult GetMessages(NotificationPullParameter param)
        {
            var result = new NotificationPullResult();
            DateTime lastNotificationTime;
            result.Messages = Notification.GetNotificationMessages(param.LastNotificationTime, out lastNotificationTime).ToArray();
            result.LastNotificationTime = lastNotificationTime;
            return new OkObjectResult(result);
        }

        private void AddBadRequest(HttpRequest req)
        {
            // add deny list
        }
    }
}
