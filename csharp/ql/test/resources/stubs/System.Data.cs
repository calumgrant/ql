using System;

namespace System.Data.SqlClient
{

    public class SqlConnection : Common.DbConnection, IDisposable
    {
        public SqlConnection() { }
        public SqlConnection(string connectionString) { }
        public void Dispose() { }
        public override string ConnectionString { get; set; }
        public override void Open() { }
        public override void Close() { }
        public override System.Data.ConnectionState State => default(System.Data.ConnectionState);
        protected override System.Data.Common.DbCommand CreateDbCommand() => null;

        protected override System.Data.Common.DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => null;
        public override void ChangeDatabase(string databaseName) { }
        public override string ServerVersion => null;
        public override string Database => "";
        public override string DataSource => "";
    }

    public class SqlCommand : Common.DbCommand
    {
        public SqlCommand(string s) { }
        public SqlCommand(string s, SqlConnection t) { }

        public SqlDataReader ExecuteReader() => null;
        public override string CommandText { get; set; }
        public override int CommandTimeout { get; set; }
        public override System.Data.CommandType CommandType { get; set; }
        protected override System.Data.Common.DbParameter CreateDbParameter() => default(System.Data.Common.DbParameter);
        protected override System.Data.Common.DbConnection DbConnection { get; set; }
        protected override System.Data.Common.DbParameterCollection DbParameterCollection => default(System.Data.Common.DbParameterCollection);
        protected override System.Data.Common.DbTransaction DbTransaction { get; set; }
        public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
        public override void Cancel() { }
        public override bool DesignTimeVisible { get; set; }
        protected override System.Data.Common.DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => null;
        public override int ExecuteNonQuery() => 0;
        public override object ExecuteScalar() => default(object);
        public override void Prepare() { }
    }

    public abstract class SqlDataReader : Common.DbDataReader, IDataReader, IDataRecord
    {
        public override string GetString(int i) => "";
    }

    public class SqlDataAdapter : Common.DbDataAdapter, IDbDataAdapter, IDataAdapter
    {
        public SqlDataAdapter(string a, SqlConnection b) { }
        public void Fill(DataSet ds) { }
        public SqlCommand SelectCommand { get; set; }
    }

    public class SqlParameter : Common.DbParameter, IDbDataParameter, IDataParameter
    {
        public SqlParameter(string s, object o) { }

        public override System.Data.DbType DbType { get; set; }
        public override System.Data.ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; }
        public override int Size { get; set; }
        public override string SourceColumn { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override object Value { get; set; }
        public override void ResetDbType() { }
    }

    public abstract class SqlParameterCollection : Common.DbParameterCollection
    {
    }

    public class SqlConnectionStringBuilder : Common.DbConnectionStringBuilder
    {
    }

    public class SqlException : Common.DbException
    {
    }
}

namespace System.Data
{
    public interface IDbDataParameter
    {
    }

    public interface IDbConnection
    {
        string ConnectionString { get; set; }
    }

    public interface IDataRecord
    {
        string GetString(int i);
    }

    public interface IDbCommand
    {
        IDataReader ExecuteReader();
        CommandType CommandType { get; set; }
        IDataParameterCollection Parameters { get; set; }
        string CommandText { get; set; }
    }

    public interface IDataReader
    {
        bool Read();
        void Close();
        string GetString(int i);
    }


    public interface IDataAdapter
    {
    }

    public interface IDbDataAdapter
    {
    }

    public interface IDataParameter
    {
    }

    public interface IDataParameterCollection
    {
        void Add(object obj);
    }
}

namespace System.Data.OleDb
{

    public abstract class OleDbConnection : Common.DbConnection, IDisposable
    {
        public OleDbConnection(string s) { }
        void IDisposable.Dispose() { }
        public override void Open() { }
        public override void Close() { }
    }

    public abstract class OleDbDataReader : Common.DbDataReader
    {
        public override bool Read() => false;
        public void Close()
        {
        }

        public override string GetString(int x) => null;

        public override object this[string s] => null;
    }

    public abstract class OleDbCommand : Common.DbCommand
    {
        public OleDbCommand(string e, OleDbConnection c)
        {
        }

        public OleDbDataReader ExecuteReader() => null;
    }
}
