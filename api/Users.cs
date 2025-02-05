using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Documents.Client;
using System.Collections.Generic;
using NotifySlackOfWebMeetingsAdmin.Apis.Entities;
using NotifySlackOfWebMeetingsAdmin.Apis.Queries;
using Microsoft.Azure.Documents.Linq;
using System.Linq;
using FluentValidation;
using Newtonsoft.Json.Serialization;
using Microsoft.Azure.Documents;
using User = NotifySlackOfWebMeetingsAdmin.Apis.Entities.User;

namespace NotifySlackOfWebMeetingsAdmin.Apis
{
    public static class Users
    {
        #region ユーザーの登録

        /// <summary>
        /// ユーザーを追加する
        /// </summary>
        /// <returns>登録したユーザー情報</returns>        
        [FunctionName("AddUsers")]
        public static async Task<IActionResult> AddUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Users")] HttpRequest req,
            [CosmosDB(
                databaseName: "Notify-Slack-of-web-meetings-db",
                collectionName: "Users",
                ConnectionStringSetting = "CosmosDbConnectionString")]IAsyncCollector<dynamic> documentsOut,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            string message = string.Empty;

            try
            {
                log.LogInformation("POST users");

                // リクエストのBODYからパラメータ取得
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                // エンティティに設定
                var user = new User()
                {
                    Name = data?.name,
                    EmailAddress = data?.emailAddress
                };

                // 入力値チェックを行う
                var validator = new UserValidator();
                validator.ValidateAndThrow(user);

                // ユーザー情報を登録
                message = await AddUsers(documentsOut, user);
            }

            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);

            }

            return new OkObjectResult(message);
        }

        /// <summary>
        /// ユーザーを追加
        /// </summary>
        /// <param name="documentsOut">CosmosDBのドキュメント</param>
        /// <param name="user">Slackチャンネル情報</param>
        /// <returns>登録したSlackチャンネル情報</returns>
        private static async Task<string> AddUsers(
        IAsyncCollector<dynamic> documentsOut,
        User user)
        {
            // Add a JSON document to the output container.
            string documentItem = JsonConvert.SerializeObject(user, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
            await documentsOut.AddAsync(documentItem);
            return 
            documentItem;
        }


        #endregion

        #region ユーザーの取得

        [FunctionName("GetUsers")]
        public static async Task<IActionResult> GetUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Users")] HttpRequest req,
        [CosmosDB(
            databaseName: "Notify-Slack-of-web-meetings-db",
            collectionName: "Users",
            ConnectionStringSetting = "CosmosDbConnectionString"
        )]DocumentClient client,
        ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            string message = string.Empty;

            try
            {
                log.LogInformation("GET Users");

                // クエリパラメータから検索条件パラメータを設定
                UsersQueryParameter queryParameter = new UsersQueryParameter()
                {
                    Ids = req.Query["ids"],
                    Name = req.Query["name"],
                    EmailAddress = req.Query["emailAddress"]
                };

                // Slackチャンネル情報を取得
                message = JsonConvert.SerializeObject(await GetUsersAsync(client, queryParameter, log));
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }

            return new OkObjectResult(message);
        }

        /// <summary>
        /// ユーザーを取得する。
        /// </summary>
        /// <param name="client">CosmosDBのドキュメントクライアント</param>
        /// <param name="queryParameter">抽出条件パラメータ</param>
        /// <param name="log">ロガー</param>
        /// <returns>ユーザー一覧</returns>
        internal static async Task<IEnumerable<User>> GetUsersAsync(
            DocumentClient client,
            UsersQueryParameter queryParameter,
            ILogger log)
        {
            // Get a JSON document from the container.
            Uri collectionUri = UriFactory.CreateDocumentCollectionUri("Notify-Slack-of-web-meetings-db", "Users");
            IDocumentQuery<User> query = client.CreateDocumentQuery<User>(collectionUri, new FeedOptions { EnableCrossPartitionQuery = true, PopulateQueryMetrics = true })
                .Where(queryParameter.GetWhereExpression())
                .AsDocumentQuery();
            log.LogInformation(query.ToString());

            var documentItems = new List<User>();
            while (query.HasMoreResults)
            {
                foreach (var documentItem in await query.ExecuteNextAsync<User>())
                {
                    documentItems.Add(documentItem);
                }
            }
            
            return documentItems;
        }

        #endregion

        #region ユーザーの削除
        
        /// <summary>
        /// ユーザー情報を削除する。
        /// </summary>
        /// <param name="req">HTTPリクエスト。</param>
        /// <param name="client">CosmosDBのドキュメントクライアント。</param>
        /// <param name="log">ロガー。</param>
        /// <returns>削除したユーザー情報。</returns>
        [FunctionName("DeleteUserById")]
        public static async Task<IActionResult> DeleteUserById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Users/{id}")] HttpRequest req,
            [CosmosDB(
                databaseName: "Notify-Slack-of-web-meetings-db",
                collectionName: "Users",
                ConnectionStringSetting = "CosmosDbConnectionString")
                ]DocumentClient client,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            string message = string.Empty;

            try
            {
                string id = req.RouteValues["id"].ToString();
                log.LogInformation($"DELETE users/{id}");

                // ユーザー情報を削除
                var documentItems = await DeleteUserById(client, id, log);

                if(!documentItems.Any())
                {
                    return new NotFoundObjectResult($"Target item not found. Id={id}");
                }
                message = JsonConvert.SerializeObject(documentItems);

            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }

            return new OkObjectResult(message);
        }

         /// <summary>
        /// ユーザー情報を削除する。
        /// </summary>
        /// <param name="client">CosmosDBのドキュメントクライアント。</param>
        /// <param name="ids">削除するユーザー情報のID一覧。</param>
        /// <param name="log">ロガー。</param>
        /// <returns>削除したユーザー情報。</returns>
        private static async Task<IEnumerable<User>> DeleteUserById(
            DocumentClient client,
            string ids,
            ILogger log)
        {
            // 事前に存在確認後に削除

            // クエリパラメータに削除するユーザー情報のIDを設定
            UsersQueryParameter queryParameter = new UsersQueryParameter()
            {
                Ids = ids,
            };

            // ユーザー情報を取得
            var documentItems = await GetUsersAsync(client, queryParameter, log);
            foreach (var documentItem in documentItems)
            {
                // ユーザー情報を削除
                // Delete a JSON document from the container.
                Uri documentUri = UriFactory.CreateDocumentUri("Notify-Slack-of-web-meetings-db", "Users", documentItem.Id);
                await client.DeleteDocumentAsync(documentUri, new RequestOptions() { PartitionKey = new PartitionKey(documentItem.Id) });
            }

            return documentItems;
        }

        #endregion
    }
}
