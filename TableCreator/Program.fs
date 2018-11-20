open FSharp.Data
open System.Xml.Linq
open System
open System.IO
open Oracle.ManagedDataAccess
open System.Data
open System.Data.SqlTypes
open System.Text.RegularExpressions

type Config = XmlProvider<"""<?xml version="1.0"?>
<config userName="EDE">
  <table name="VERPROBUNG" fullName="Verprobung">
    <column name="NR" type="NUMBER(12)" COMMENT="eindeutige Nr. (PK)"></column>
    <column name="PROBEABRECH_NR" type="NUMBER(12)" COMMENT="Foreign key zu nr(PROBEABRECH)"></column>
    <column name="PFLSTA_NR " type="NUMBER(10)" COMMENT="Foreign key zu nr(PFLSTA)"></column>
    <column name="QUITDATUM " type="DATE" COMMENT="Quittierungs-Datum"></column>
    <column name="TITEL " type="VARCHAR2(100)" COMMENT="Titel"></column>
    <column name="MANDANT_NR " type="NUMBER(5)" COMMENT="Foreign key zu nr(KENMDT), für Policy-Mandant"></column>
  </table>
  <table name="FIBELE" fullName="Fibele">
    <column name="NR" type="NUMBER(12)" COMMENT="eindeutige Nr. (PK)"></column>
    <column name="PROBEABRECH_NR" type="NUMBER(12)" COMMENT="Foreign key zu nr(PROBEABRECH)"></column>
    <column name="PFLSTA_NR " type="NUMBER(10)" COMMENT="Foreign key zu nr(PFLSTA)"></column>
    <column name="QUITDATUM " type="DATE" COMMENT="Quittierungs-Datum"></column>
    <column name="TITEL " type="VARCHAR2(100)" COMMENT="Titel"></column>
    <column name="MANDANT_NR " type="NUMBER(5)" COMMENT="Foreign key zu nr(KENMDT), für Policy-Mandant"></column>
    <copyFrom tableName="FIBELE">
      <ignoreColumn>nr</ignoreColumn>
    </copyFrom>
    <copyFrom tableName="FIBELE">
      <ignoreColumn>nr</ignoreColumn>
    </copyFrom>
  </table>
</config>






 """>

 type Column =
    { table : string
      name : string
      cType : string
      nullable : string
      fkTable : string
      fkColumn : string
      comment : string
    }


let config = Config.Load("config.xml")

let createTableTemplate = "createTable.txt"
let createSequenceTemplate = "createSequence.txt"
let addPrimaryKeysFile = "addPrimaryKeys.txt"
let addPrimaryKeysFileNew = "addPrimaryKeysNew.txt"

let addForeignKeysFile = "addForeignKeys.txt"
let addForeignKeysFileNew = "addForeignKeysNew.txt"

let addIndicesFile = "addIndices.txt"
let addIndicesFileNew = "addIndicesNew.txt"

File.Copy(addPrimaryKeysFile, addPrimaryKeysFileNew, true)
File.Copy(addForeignKeysFile, addForeignKeysFileNew, true)
File.Copy(addIndicesFile, addIndicesFileNew, true)


[<EntryPoint>]
let main argv = 
    let connStr  = """user id=senso;password=senso;data source=ENTWV7"""
    use conn = new Oracle.ManagedDataAccess.Client.OracleConnection(connStr)
    conn.Open()

    for table in config.Tables do
        let createTableFile = @"C:\workspace\APEX_VERW7\entw\sqlscri\ht" + (table.Name.ToLower()) + "_v7.sql"
        let createSequenceFile = @"C:\workspace\APEX_VERW7\entw\sqlscri\hs" + (table.Name.ToLower()) + "_v7.sql"

        let columns =
            [ for column in table.Columns do
                yield
                    { name = column.Name
                      cType = column.Type
                      nullable = "NOT NULL"//column.Nullable
                      table = table.Name
                      fkTable = ""
                      fkColumn = ""
                      comment = "-- " + column.Comment
                    }
            ]
            @
            [ for copyFrom in table.CopyFroms do
                use command = conn.CreateCommand(CommandText =
                    """SELECT
    atc.column_name,
    atc.data_type,
    case atc.data_type when 'NUMBER' then atc.data_precision || (case atc.data_scale when 0 then '' else ',' || atc.data_scale END) else '' || data_length END AS atcPrec,
    atc.nullable,
    a.table_name,
    c_pk.table_name r_table_name,
    a2.column_name
    FROM all_tab_cols atc
LEFT JOIN all_cons_columns a ON a.table_name = atc.table_name AND a.owner = atc.owner AND a.column_name = atc.column_name
LEFT JOIN all_constraints c ON a.owner = c.owner AND a.constraint_name = c.constraint_name-- AND atc.column_name = a.column_name
LEFT JOIN all_constraints c_pk ON c.r_owner = c_pk.owner AND c.r_constraint_name = c_pk.constraint_name
LEFT JOIN all_cons_columns a2 ON c_pk.constraint_name = a2.constraint_name
WHERE lower(atc.table_name) = lower('""" + table.Name + """')
AND atc.owner = 'SENSO41'
AND c.constraint_type IS NULL OR (c.constraint_type = 'R' AND lower(a.table_name) = lower('""" + table.Name + """')  AND c.owner = 'SENSO41')
GROUP BY atc.column_name,
    atc.data_type,
    case atc.data_type when 'NUMBER' then atc.data_precision || (case atc.data_scale when 0 then '' else ',' || atc.data_scale END) else '' || data_length END,
    atc.nullable,
    a.table_name,
    c_pk.table_name,
    a2.column_name""")
                use reader = command.ExecuteReader()
                let a = reader.Read()
                yield!
                    ([ while reader.Read() do
                        let t =
                            match reader.[2] with
                            | null -> ""
                            | x when x.ToString() = "," -> ""
                            | x -> reader.GetString(1) + "(" + (x.ToString()) + ")"
                        let  nullable =
                            match reader.[3] with
                            | null -> "NOT NULL"
                            | x when x.ToString() = "Y" -> "NULL"
                            | _ -> "NOT NULL"
                        let columnName = reader.GetString(0)
                        yield
                            { name = columnName
                              cType = t
                              nullable = nullable
                              table = table.Name
                              fkTable = (match reader.[5] with null -> "" | _ -> string(reader.[5]))
                              fkColumn = (match reader.[6] with null -> "" | _ -> string(reader.[6]))
                              comment = 
                                let lines = File.ReadAllLines(@"C:\workspace\SENSO\entw\sqlscri\ht" + copyFrom.TableName + ".sql") |> List.ofSeq
                                let lines = lines |> List.skipWhile (fun l -> not (Regex.Match(l, @"^\s*CREATE\s+TABLE\s+" + copyFrom.TableName + "(\s+|$)", RegexOptions.IgnoreCase).Success))
                                let lines = lines |> List.skipWhile (fun l -> not (Regex.Match(l, @"^\s*" + columnName + "", RegexOptions.IgnoreCase).Success))
                                match lines with
                                | [] -> ""
                                | (x::xs) ->
                                  let s =
                                    (x::(xs |> List.takeWhile (fun l -> (Regex.Match(l, @"^\s*(--)").Success))))
                                    |> List.mapi (fun i x ->
                                        let m = Regex.Match(x, ".*?--\s*(.*)")
                                        if m.Success
                                        then
                                            if i = 0
                                            then "-- " + m.Groups.[1].Value
                                            else "                                                 -- " + m.Groups.[1].Value
                                        else "")
                                    |> String.concat "\r\n"
                                  s
                            }
                    ] |> List.filter (fun c -> c.cType <> ""))
            ]

        let columnToString col =
            "    " + (col.name.PadRight(17)) + " " + (col.cType.PadRight(15)) + " " + (col.nullable.PadRight(10)) + " " + col.comment

        let columnsToString cols =
            let columnStrings = List.map columnToString (cols |> List.mapi (fun i col -> if i = cols.Length - 1 then col else { col with nullable = col.nullable + ","} ))
            String.concat "\r\n" columnStrings
            
        
        let columnsThatAreForeignKeys cols =
            cols |> List.filter (fun col -> col.fkTable <> "") 

        let createTableTxt =
            File.ReadAllText(createTableTemplate)
                .Replace("{{TableName}}", table.Name)
                .Replace("{{FullTableName}}", table.FullName)
                .Replace("{{UserName}}", config.UserName)
                .Replace("{{Columns}}", columnsToString columns)
        File.WriteAllText(createTableFile, createTableTxt)

        let createTableTxt =
            File.ReadAllText(createSequenceTemplate)
                .Replace("{{TableName}}", table.Name)
                .Replace("{{FullTableName}}", table.FullName)
                .Replace("{{UserName}}", config.UserName)
        File.WriteAllText(createSequenceFile, createTableTxt)

        let mutable added = false
        let addPrimaryKeysLines = File.ReadAllLines(addPrimaryKeysFileNew)
        [ for addPrimaryKeysLine in addPrimaryKeysLines do
            let m = Regex.Match(addPrimaryKeysLine, @"^\s*ALTER TABLE ([^\s]+)\s", RegexOptions.IgnoreCase)
            yield
                if not added && (addPrimaryKeysLine.Trim().ToLower() = "spool off" || (m.Success && m.Groups.[1].Value > table.Name)) then
                    added <- true
                    "ALTER TABLE " + table.Name + " ADD CONSTRAINT PK_" + table.Name + " PRIMARY KEY (NR) USING INDEX TABLESPACE &SITBLSPC.;" + addPrimaryKeysLine
                else addPrimaryKeysLine
        ]  
        |> fun txt -> File.WriteAllLines(addPrimaryKeysFileNew, txt)

        added <- false
        let addForeignKeysLines = File.ReadAllLines(addForeignKeysFileNew)
        [ for addForeignKeysLine in addForeignKeysLines do
            let m = Regex.Match(addForeignKeysLine, @"^\s*ALTER TABLE ([^\s]+)\s", RegexOptions.IgnoreCase)
            yield
                if not added && (addForeignKeysLine.Trim().ToLower() = "spool off" || (m.Success && m.Groups.[1].Value > table.Name)) then
                    added <- true
                    ([ for column in columns |> columnsThatAreForeignKeys do
                        yield "ALTER TABLE " + table.Name + " ADD CONSTRAINT FK_" + table.Name + "_" + column.fkTable + " FOREIGN KEY (" + column.name + ") REFERENCES " + column.fkTable + ";";
                    ] |> (String.concat "\r\n")) + addForeignKeysLine
                else addForeignKeysLine
        ]  
        |> fun txt -> File.WriteAllLines(addForeignKeysFileNew, txt)

        added <- false
        let addIndicesLines = File.ReadAllLines(addIndicesFileNew)
        [ for addIndicesLine in addIndicesLines do
            let m = Regex.Match(addIndicesLine, @"^\s*CREATE\s+(UNIQUE\s+)?INDEX\s+[^\s]+\s+ON\s+([^\s]+)\s\(.*?\)\s+TABLESPACE\s+", RegexOptions.IgnoreCase)
            yield
                if not added && (addIndicesLine.Trim().ToLower() = "spool off" || (m.Success && m.Groups.[2].Value > table.Name)) then
                    added <- true
                    ([ for column in columnsThatAreForeignKeys columns do
                        yield "CREATE        INDEX " + ((table.Name + "_" + column.fkTable).PadRight(33)) + " ON " + table.Name + " (" + column.fkColumn + ") TABLESPACE &SITBLSPC.;";
                    ] |> (String.concat "\r\n")) + addIndicesLine
                else addIndicesLine
        ]  
        |> fun txt -> File.WriteAllLines(addIndicesFileNew, txt)

    printfn "%A" argv
    0





