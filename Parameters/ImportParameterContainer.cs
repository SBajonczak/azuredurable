using System.Collections.Generic;

namespace SBA.Durable.Parameters
{
    public class ImportParametersContainer
    {
        public List<ImportParameters> Importings { get; set; }
        public string NotifierMail { get; set; }
        public string InstanceId { get; set; }
        public ImportParametersContainer()
        {
            this.Importings = new List<ImportParameters>();
        }
        public ImportParametersContainer(List<ImportParameters> importing, string notifierMail) : this()
        {
            this.Importings = importing;
            this.NotifierMail = notifierMail;

        }

    }
}