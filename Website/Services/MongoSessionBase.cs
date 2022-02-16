using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Website.Services
{
    /**
     * 
     */
    public class MongoSessionBase
    {
        private readonly MongoService _mongo;
        protected MongoSessionBase(MongoService mongo)
        {
            this._mongo = mongo;
        }

        protected async Task<IClientSessionHandle> GetSessionHandle() =>
            await this._mongo.GetSessionHandle();
    }
}
