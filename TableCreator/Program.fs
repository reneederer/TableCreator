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
    <column name="NR" type="NUMBER(12)" nullable="NOT NULL" comment="eindeutige Nr. (PK)" createIndex="false"></column>
    <column name="PROBEABRECH_NR" type="NUMBER(12)" nullable="NOT NULL" fkTable="PROBEABRECH" fkColumn="nr" comment="Foreign key zu nr(PROBEABRECH)"></column>
  </table>
  <table name="FIBELE" fullName="Fibele">
    <column name="NR" type="NUMBER(12)" nullable="NOT NULL" comment="eindeutige Nr. (PK)"></column>
    <column name="PROBEABRECH_NR" type="NUMBER(12)" nullable="NOT NULL" comment="Foreign key zu nr(PROBEABRECH)"></column>
    <copyFrom tableName="FIBELE">
      <ignoreColumn>nr</ignoreColumn>
    </copyFrom>
    <copyFrom tableName="FIBELE">
      <ignoreColumn>nr</ignoreColumn>
      <ignoreColumn>nr</ignoreColumn>
      <remapColumn oldName="nr" newName="FIBELE_NR" newComment="Foreign key zu nr(FIBELE)" createForeignKey="false" createIndex="false"/>
      <remapColumn oldName="nr" newName="FIBELE_NR" newComment="Foreign key zu nr(FIBELE)" createForeignKey="false" createIndex="false"/>
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
      createIndex : bool
      comment : string
    }


let config = Config.Load(@"C:\Users\ederer\source\repos\TableCreator\TableCreator\config.xml")

let createTableTemplate = "createTable.txt"
let createSequenceTemplate = "createSequence.txt"

let addPrimaryKeysFile = @"C:\workspace\APEX_VERW7\entw\sqlscri\chp_v7.sql"
let addPrimaryKeysFileNew = @"C:\workspace\APEX_VERW7\entw\sqlscri\chp_v7.sql"

let addForeignKeysFile = @"C:\workspace\APEX_VERW7\entw\sqlscri\chr_v7.sql"
let addForeignKeysFileNew = @"C:\workspace\APEX_VERW7\entw\sqlscri\chr_v7.sql"

let addIndicesFile = @"C:\workspace\APEX_VERW7\entw\sqlscri\chx_v7.sql"
let addIndicesFileNew = @"C:\workspace\APEX_VERW7\entw\sqlscri\chx_v7.sql"




[<EntryPoint>]
let main argv = 
    (*
    File.Copy(addPrimaryKeysFile, addPrimaryKeysFileNew, true)
    File.Copy(addForeignKeysFile, addForeignKeysFileNew, true)
    File.Copy(addIndicesFile, addIndicesFileNew, true)
    *)
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
                      nullable = column.Nullable
                      table = table.Name
                      fkTable =
                        column.FkTable |> Option.defaultValue ""
                      fkColumn = column.FkColumn |> Option.defaultValue ""
                      comment = "-- " + column.Comment
                      createIndex = column.CreateIndex |> Option.defaultValue true
                    }
            ]
            @
            [ for copyFrom in table.CopyFroms do
                use command = conn.CreateCommand(CommandText =
                    """WITH myTable AS (
SELECT
    c.constraint_type,
    atc.column_name,
    atc.data_type,
    case atc.data_type when 'NUMBER' then atc.data_precision || (case atc.data_scale when 0 then '' else ',' || atc.data_scale END) else '' || data_length END AS atcPrec,
    atc.nullable,
    a.table_name,
    c_pk.table_name fk_table_name,
    a2.column_name fk_column_name
    FROM all_tab_cols atc
LEFT JOIN all_cons_columns a ON a.table_name = atc.table_name AND a.owner = atc.owner
LEFT JOIN all_constraints c ON a.owner = c.owner AND a.constraint_name = c.constraint_name AND a.column_name = atc.column_name AND c.table_name = a.table_name
LEFT JOIN all_constraints c_pk ON c.r_owner = c_pk.owner AND c.r_constraint_name = c_pk.constraint_name
LEFT JOIN all_cons_columns a2 ON c_pk.constraint_name = a2.constraint_name
WHERE lower(atc.table_name) = lower('""" + copyFrom.TableName + """') AND atc.owner = 'SENSO41'
AND (c.constraint_type IS NULL OR (c.constraint_type = 'R' AND lower(atc.table_name) = lower('""" + copyFrom.TableName + """')  AND c.owner = 'SENSO41' AND atc.column_name = a.column_name))
   AND atc.column_name NOT LIKE 'SYS_%' AND atc.column_name <> 'ORA_ARCHIVE_STATE'

GROUP BY
    c.constraint_type,
    atc.column_name,
    atc.data_type,
    case atc.data_type when 'NUMBER' then atc.data_precision || (case atc.data_scale when 0 then '' else ',' || atc.data_scale END) else '' || data_length END,
    atc.nullable,
    a.table_name,
    c_pk.table_name,
    a2.column_name
)
SELECT
    column_name,
    data_type,
    atcPrec,
    nullable,
    table_name,
    fk_table_name,
    fk_column_name FROM myTable m1 WHERE
constraint_type = 'R' OR (NOT EXISTS (SELECT 'x' FROM myTable m2 WHERE column_name = m1.column_name AND constraint_type = 'R'))
ORDER BY column_name""")
                use reader = command.ExecuteReader()
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
                        if columnName.ToLower() = "nr" then
                            ()
                        yield
                            if (copyFrom.IgnoreColumns |> Seq.map(fun x -> x.ToLower()) |> Seq.contains(columnName.ToLower())) ||
                               (Regex.Match(columnName, @"^SYS_.*\$$", RegexOptions.IgnoreCase)).Success
                            then None
                            elif copyFrom.RemapColumns |> Seq.map(fun x -> x.OldName.ToLower()) |> Seq.contains(columnName.ToLower()) then
                                let remap = copyFrom.RemapColumns |> Seq.find (fun x -> x.OldName.ToLower() = columnName.ToLower()) 
                                Some
                                    { name = remap.NewName
                                      cType = t
                                      nullable = nullable
                                      table = table.Name
                                      fkTable = if remap.CreateForeignKey then (match reader.[5] with null -> "" | _ -> string(reader.[5])) else ""
                                      fkColumn = if remap.CreateForeignKey then (match reader.[6] with null -> "" | _ -> string(reader.[6])) else ""
                                      createIndex = remap.CreateIndex
                                      comment =  "-- " + remap.NewComment
                                    }
                            else
                              Some
                                { name = reader.GetString(0)
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
                                  createIndex = true
                                }
                    ] |> List.choose (fun x -> x) |> List.filter (fun c -> c.cType <> "" && c.name <> ""))
            ]

        let columnToString col =
            "    " + (col.name.PadRight(17)) + " " + (col.cType.PadRight(15)) + " " + (col.nullable.PadRight(10)) + " " + col.comment

        let columnsToString cols =
            let columnStrings = List.map columnToString (cols |> List.mapi (fun i col -> if i = cols.Length - 1 then col else { col with nullable = col.nullable + ","} ))
            String.concat "\r\n" columnStrings
            
        
        let columnsThatAreForeignKeys cols =
            cols |> List.filter (fun col -> col.fkTable <> "") 
        
        let columnsThatShouldBeIndexed (cols : List<Column>) =
            cols |> List.filter (fun col -> col.fkTable <> "" && col.createIndex)

        let createTableTxt =
            File.ReadAllText(createTableTemplate)
                .Replace("{{TableName}}", table.Name)
                .Replace("{{FullTableName}}", table.FullName)
                .Replace("{{UserName}}", config.UserName)
                .Replace("{{Columns}}", columnsToString columns)
                .Replace("{{CurrentDate}}", DateTime.Today.ToString("dd.MM.yyyy"))
        File.WriteAllText(createTableFile, createTableTxt)

        let createSequenceTxt =
            File.ReadAllText(createSequenceTemplate)
                .Replace("{{TableName}}", table.Name)
                .Replace("{{FullTableName}}", table.FullName)
                .Replace("{{UserName}}", config.UserName)
                .Replace("{{CurrentDate}}", DateTime.Today.ToString("dd.MM.yyyy"))
        File.WriteAllText(createSequenceFile, createSequenceTxt)

        let mutable added = false
        let addPrimaryKeysLines = File.ReadAllLines(addPrimaryKeysFileNew)
        [ for addPrimaryKeysLine in addPrimaryKeysLines do
            let m = Regex.Match(addPrimaryKeysLine, @"^\s*ALTER TABLE ([^\s]+)\s", RegexOptions.IgnoreCase)
            yield
                if not added && (addPrimaryKeysLine.Trim().ToLower() = "spool off" || (m.Success && m.Groups.[1].Value > table.Name)) then
                    added <- true
                    "ALTER TABLE " + table.Name + " ADD CONSTRAINT PK_" + table.Name + " PRIMARY KEY (&MDTNR.NR) USING INDEX TABLESPACE &SITBLSPC.;\r\n\r\n" + addPrimaryKeysLine
                else addPrimaryKeysLine
        ]  
        |> fun lines -> File.WriteAllLines(addPrimaryKeysFileNew, lines)

        added <- false
        let addForeignKeysLines = File.ReadAllLines(addForeignKeysFileNew)
        [ for addForeignKeysLine in addForeignKeysLines do
            let m = Regex.Match(addForeignKeysLine, @"^\s*ALTER TABLE ([^\s]+)\s", RegexOptions.IgnoreCase)
            yield
                (if not added && (addForeignKeysLine.Trim().ToLower() = "spool off" || (m.Success && m.Groups.[1].Value > table.Name)) then
                    added <- true
                    ([ for column in columns |> columnsThatShouldBeIndexed do
                        yield "ALTER TABLE " + table.Name + " ADD CONSTRAINT FK_" + table.Name + "_" + column.fkTable + " FOREIGN KEY (" + "&MDTNR." + column.name + ") REFERENCES " + column.fkTable + ";";
                    ] |> (String.concat "\r\n")) + "\r\n\r\n" + addForeignKeysLine
                else addForeignKeysLine)
        ]  
        |> fun lines -> File.WriteAllLines(addForeignKeysFileNew, lines)

        added <- false
        let addIndicesLines = File.ReadAllLines(addIndicesFileNew)
        [ for addIndicesLine in addIndicesLines do
            let m = Regex.Match(addIndicesLine, @"^\s*CREATE\s+(UNIQUE\s+)?INDEX\s+[^\s]+\s+ON\s+([^\s]+)\s\(.*?\)\s+TABLESPACE\s+", RegexOptions.IgnoreCase)
            yield
                if not added && (addIndicesLine.Trim().ToLower() = "spool off" || (m.Success && m.Groups.[2].Value > table.Name)) then
                    added <- true
                    ([ for column in columnsThatAreForeignKeys columns do
                        yield "CREATE        INDEX " + ((table.Name + "_" + column.fkTable).PadRight(33)) + " ON " + table.Name + " (" + "&MDTNR." + column.name + ") TABLESPACE &SITBLSPC.;";
                    ] |> (String.concat "\r\n")) + "\r\n\r\n" + addIndicesLine
                else addIndicesLine
        ]
        |> fun lines -> File.WriteAllLines(addIndicesFileNew, lines)

    0
