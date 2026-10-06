using System.Collections.Generic;
using SRM_by_Longtygu.Models;

namespace SRM_by_Longtygu.Repositories
{
    public interface IResourceFileRepository
    {
        IEnumerable<ResourceFile> GetAll();
        void Add(ResourceFile file);
        void Update(ResourceFile file);
        void Delete(int id);
    }
}