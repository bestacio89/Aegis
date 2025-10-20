using Aegis.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Infrastructure.Persistence.Repositories;

public interface IRuleResultRepository 
    
{


    public Task<IEnumerable<RuleResultEntity>> GetViolationsBySeverityAsync(string severity, CancellationToken token = default);
  
}
