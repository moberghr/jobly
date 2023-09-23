using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jobly.Core.Interfaces;
public interface IConcurrencyToken
{
    public Guid Version { get; set; }
}
