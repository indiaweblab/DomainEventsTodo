using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using DomainEventsTodo.Domain;
using DomainEventsTodo.Repositories.Abstract;
using DomainEventsTodo.ViewModels;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace DomainEventsTodo.Test
{
    public class TodoControllerShould : IDisposable
    {
        private readonly HttpClient _client;
        private readonly TodoVm _todo;
        private readonly string _root;
        private readonly TestServer _server;
        private readonly List<TodoMemento> _mementoes = new List<TodoMemento>();
        private readonly Mock<ITodoRepository> _repository = new Mock<ITodoRepository>();
        public TodoControllerShould()
        {
            SetupRepo();

            _server = CreateServer();

            _client = CreateClient(_server);

            _todo = new TodoVm
            {
                Description = "Bla bla bla"
            };

            _root = "api/v1/todo/";
        }

        private void SetupRepo()
        {
            _repository.Setup(r => r[It.IsAny<Guid>()])
                .Returns<Guid>(g =>
                {
                    var todo = _mementoes.FirstOrDefault(m => m.Id == g);

                    if (todo == null)
                        return null;
                    return new Todo(todo);
                });

            _repository.Setup(r => r.Add(It.IsAny<Todo>()))
                .Callback<Todo>(todo => _mementoes.Add(todo.Memento));

            _repository.Setup(r => r.Replace(It.IsAny<Todo>()))
                .Callback<Todo>(todo =>
                {
                    var index = _mementoes.FindIndex(m => m.Id == todo.Id);

                    _mementoes[index] = todo.Memento;
                });

            _repository.Setup(r => r.Remove(It.IsAny<Guid>()))
                .Callback<Guid>(guid =>
                {
                    var todo = _mementoes.FirstOrDefault(m => m.Id == guid);

                    _mementoes.Remove(todo);

                });

            _repository.Setup(r => r.GetEnumerator())
                .Returns(_mementoes.Select(m => new Todo(m)).ToList().GetEnumerator());

        }

        private TestServer CreateServer()
        {
            return new TestServer(WebHost.CreateDefaultBuilder()
                  .UseStartup<Startup>()
                  .ConfigureServices(s => s.AddScoped<ITodoRepository>(services => _repository.Object)));
        }

        private HttpClient CreateClient(TestServer server)
        {
            var client = server.CreateClient();

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }

        private string ToJson(TodoVm todo)
        {
            return JsonConvert.SerializeObject(todo);
        }

        private TodoVm FromJson(string todo)
        {
            return JsonConvert.DeserializeObject<TodoVm>(todo);
        }

        private StringContent CreateContent(TodoVm todo)
        {
            return new StringContent(ToJson(todo), Encoding.UTF8, "application/json");
        }

        private async Task<TodoVm> FromContent(HttpContent content)
        {
            return FromJson(await content.ReadAsStringAsync());
        }

        private async Task<TodoVm> Get(Guid id, HttpClient client)
        {
            var response = await client.GetAsync(_root + id);

            response.EnsureSuccessStatusCode();

            return await FromContent(response.Content);
        }

        private async Task<TodoVm> Get(Guid id)
        {
            var response = await _client.GetAsync(_root + id);

            response.EnsureSuccessStatusCode();

            return await FromContent(response.Content);
        }

        [Fact]
        public async Task Crud()
        {
            await Create();
            await ReadAll();
            await Read();
            await Update();
            await Delete();
        }

        [Fact]
        public async Task Count_Equal_1()
        {
            var todo = new TodoVm
            {
                Description = "One"
            };

            var response = await _client.PostAsync(_root, CreateContent(todo));

            response.EnsureSuccessStatusCode();

            var created = await FromContent(response.Content);

            response = await _client.GetAsync(_root + "count");

            response.EnsureSuccessStatusCode();

            var result = int.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(1, result);

            await _client.DeleteAsync(_root + created.Id);
        }

        [Fact]
        public async Task Not_Create_Duplicate_Description()
        {
            var todo = new TodoVm
            {
                Description = "Ololosh"
            };

            var response = await _client.PostAsync(_root, CreateContent(todo));

            response.EnsureSuccessStatusCode();

            var result = await FromContent(response.Content);

            var duplicate = await _client.PostAsync(_root, CreateContent(todo));

            await _client.DeleteAsync(_root + result.Id);

            Assert.NotEqual(true, duplicate.IsSuccessStatusCode);
        }

        [Fact]
        public async Task Not_Create_Too_Short_Description()
        {
            var todo = new TodoVm
            {
                Description = "O"
            };

            var response = await _client.PostAsync(_root, CreateContent(todo));

            Assert.NotEqual(true, response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task Not_Create_Empty_Description()
        {
            var todo = new TodoVm
            {
                Description = "       "
            };

            var response = await _client.PostAsync(_root, CreateContent(todo));

            Assert.NotEqual(true, response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task Not_Create_Nul_Description()
        {
            var todo = new TodoVm
            {
                Description = null
            };

            var response = await _client.PostAsync(_root, CreateContent(todo));

            Assert.NotEqual(true, response.IsSuccessStatusCode);
        }

        private async Task Create()
        {
            var response = await _client.PostAsync(_root, CreateContent(_todo));

            response.EnsureSuccessStatusCode();

            var result = await FromContent(response.Content);

            Assert.NotNull(result);
            Assert.NotEqual(default(Guid), result.Id);
            Assert.Equal(_todo.Description, result.Description);
            Assert.Equal(false, result.IsComplete);

            _todo.Id = result.Id;
        }

        private async Task ReadAll()
        {
            var result = await _client.GetAsync(_root);

            result.EnsureSuccessStatusCode();

            var array = JsonConvert.DeserializeObject<TodoVm[]>(await result.Content.ReadAsStringAsync());

            Assert.Contains(array, x => x.Id == _todo.Id);

        }

        private async Task Read()
        {
            var result = await Get(_todo.Id);

            Assert.Equal(_todo.Id, result.Id);
            Assert.Equal(_todo.Description, result.Description);
            Assert.Equal(_todo.IsComplete, result.IsComplete);
        }

        private async Task Update()
        {
            _todo.Description = "Foo Bar Baz";
            _todo.IsComplete = true;

            var response = await _client.PutAsync(_root + _todo.Id, CreateContent(_todo));

            response.EnsureSuccessStatusCode();

            var result = await Get(_todo.Id);

            Assert.Equal(_todo.Id, result.Id);
            Assert.Equal(_todo.Description, result.Description);
            Assert.NotEqual(_todo.IsComplete, result.IsComplete);
        }


        [Fact]
        public async Task MakeComplete()
        {
            var todo = new TodoVm
            {
                Description = "MakeComplete"
            };

            var url = "http://localhost:8888/";

            var server = WebHost.CreateDefaultBuilder()
                .UseStartup<Startup>()
                .UseUrls(url)
                .ConfigureServices(s => s.AddScoped<ITodoRepository>(services => _repository.Object))
                .Build();

            Task.Run(() =>
            {
                server.Start();
            });

            var connection = new HubConnectionBuilder()
                .WithUrl("http://localhost:8888/hub")
                .WithConsoleLogger()
                .Build();

            connection.On<string>("Notify", data =>
            {
                Assert.Equal(todo.Description + " is complete", data);
            });

            await connection.StartAsync();

            var client = new HttpClient();

            client.BaseAddress = new Uri(url);

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var create = await client.PostAsync(_root, CreateContent(todo));

            create.EnsureSuccessStatusCode();

            var id = (await FromContent(create.Content)).Id;

            var response = await client.PostAsync(_root + id + "/MakeComplete", new StringContent(""));

            response.EnsureSuccessStatusCode();

            var result = await Get(id, client);

            Assert.Equal(id, result.Id);
            Assert.Equal(todo.Description, result.Description);
            Assert.Equal(true, result.IsComplete);

            await client.DeleteAsync(_root + id);

        }

        private async Task Delete()
        {
            var response = await _client.DeleteAsync(_root + _todo.Id);

            response.EnsureSuccessStatusCode();

            var result = await _client.GetAsync(_root);

            result.EnsureSuccessStatusCode();

            var array = JsonConvert.DeserializeObject<TodoVm[]>(await result.Content.ReadAsStringAsync());

            Assert.DoesNotContain(array, x => x.Id == _todo.Id);
        }

        [Fact]
        public async Task Get_Not_Accept_Default_Id()
        {
            var result = await _client.GetAsync(_root + default(Guid));

            Assert.True(!result.IsSuccessStatusCode);
        }

        [Fact]
        public async Task Delete_Accept_Default_Id()
        {
            var result = await _client.DeleteAsync(_root + default(Guid));

            result.EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Put_Not_Accept_Default_Id()
        {
            var result = await _client.PutAsync(_root + default(Guid), CreateContent(_todo));

            Assert.True(!result.IsSuccessStatusCode);
        }

        [Fact]
        public async Task MakeComplete_Not_Accept_Default_Id()
        {
            var result = await _client.PostAsync(_root + default(Guid) + "/MakeComplete", new StringContent(""));

            Assert.True(!result.IsSuccessStatusCode);
        }

        public void Dispose()
        {
            _client.Dispose();
            _server.Dispose();
        }
    }
}