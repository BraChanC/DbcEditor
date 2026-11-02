namespace DbcEditor.Services.Parsing;

/// <summary>
/// LALR grammar for Vector DBC, embedded so the project does not ship a .lark file.
/// Statement keywords are named terminals (priority above IDENT) so the contextual lexer
/// can still treat the same spelling as an identifier in NS_ / node / signal names.
/// </summary>
static class DbcLarkGrammar
{
    public const string Text = """
        start: dbc
        dbc: stmt*
        ?stmt: version | ns | bs | bu | message | cm | ba_def_def | ba_def | ba | val_table | val | sig_group | sig_valtype | ev | ev_data | bo_tx | sg_mul | other

        version: VERSION STRING ";"?
        ns: NS_ ":"? ns_item*
        ns_item: IDENT | CM_ | BA_DEF_DEF_REL_ | BA_DEF_DEF_ | BA_DEF_REL_ | BA_DEF_SGTYPE_ | BA_DEF_ | BA_REL_ | BA_SGTYPE_ | BA_ | VAL_TABLE_ | VAL_ | SIG_GROUP_ | SIG_VALTYPE_ | SIGTYPE_VALTYPE_ | EV_DATA_ | ENVVAR_DATA_ | EV_ | BO_TX_BU_ | SG_MUL_VAL_ | SGTYPE_VAL_ | SGTYPE_ | SIG_TYPE_REF_ | BU_SG_REL_ | BU_BO_REL_ | BU_EV_REL_
        bs: BS_ ":"? (NUMBER | IDENT)* ";"?
        bu: BU_ ":"? IDENT* ";"?
        message: BO_ NUMBER IDENT ":" NUMBER IDENT signal*
        signal: SG_ IDENT mux? ":" NUMBER "|" NUMBER "@" NUMBER SIGN? "(" signed "," signed ")" "[" signed "|" signed "]" STRING receivers?
        mux: MM | MUX
        receivers: IDENT ("," IDENT)*
        signed: SIGN? NUMBER

        cm: CM_ cm_body ";"?
        cm_body: BU_ IDENT STRING | BO_ NUMBER STRING | SG_ NUMBER IDENT STRING | EV_ IDENT STRING | STRING

        ba_def_def: BA_DEF_DEF_ STRING attr_value ";"? | BA_DEF_DEF_REL_ STRING attr_value ";"?
        ba_def: BA_DEF_ obj? STRING ba_type ";"? | BA_DEF_REL_ obj? STRING ba_type ";"? | BA_DEF_SGTYPE_ STRING ba_type ";"?
        obj: BU_SG_REL_ | BU_BO_REL_ | BU_EV_REL_ | BU_ | BO_ | SG_ | EV_
        ba_type: ENUM STRING ("," STRING)* | TYPEKW signed signed? | TYPEKW
        ba: BA_ STRING ba_body ";"?
        ba_body: BU_ IDENT attr_value | BO_ NUMBER attr_value | SG_ NUMBER IDENT attr_value | EV_ IDENT attr_value | attr_value
        attr_value: STRING | signed

        val_table: VAL_TABLE_ IDENT val_pair* ";"?
        val: VAL_ val_body ";"?
        val_body: EV_ IDENT val_pair+ | NUMBER IDENT val_pair+ | IDENT val_pair+
        val_pair: NUMBER STRING
        sig_group: SIG_GROUP_ NUMBER IDENT NUMBER ":" IDENT* ";"?
        sig_valtype: SIG_VALTYPE_ NUMBER IDENT ":"? NUMBER ";"?
        ev: EV_ IDENT ":" NUMBER "[" signed "|" signed "]" STRING signed NUMBER IDENT IDENT* ";"?
        ev_data: ENVVAR_DATA_ IDENT ":" NUMBER ";"? | EV_DATA_ IDENT ":" NUMBER ";"?
        bo_tx: BO_TX_BU_ NUMBER ":" IDENT ("," IDENT)* ";"?
        sg_mul: SG_MUL_VAL_ NUMBER IDENT IDENT mul_range ("," mul_range)* ";"?
        mul_range: NUMBER "-" NUMBER | NUMBER

        other: IDENT atom* ";"?
        atom: IDENT | NUMBER | STRING | ":" | "," | "|" | "@" | SIGN | "(" | ")" | "[" | "]" | MM

        MM.4: "M"
        MUX.4: /m[0-9]+M?/i
        SIGN.3: "+" | "-"
        TYPEKW.3: "INT" | "HEX" | "FLOAT" | "STRING"
        ENUM.3: "ENUM"

        BA_DEF_DEF_REL_.3: "BA_DEF_DEF_REL_"
        BA_DEF_DEF_.3: "BA_DEF_DEF_"
        BA_DEF_SGTYPE_.3: "BA_DEF_SGTYPE_"
        BA_DEF_REL_.3: "BA_DEF_REL_"
        BA_DEF_.3: "BA_DEF_"
        BA_SGTYPE_.3: "BA_SGTYPE_"
        BA_REL_.3: "BA_REL_"
        BA_.3: "BA_"
        VAL_TABLE_.3: "VAL_TABLE_"
        SIG_VALTYPE_.3: "SIG_VALTYPE_"
        SIGTYPE_VALTYPE_.3: "SIGTYPE_VALTYPE_"
        SIG_GROUP_.3: "SIG_GROUP_"
        SIG_TYPE_REF_.3: "SIG_TYPE_REF_"
        ENVVAR_DATA_.3: "ENVVAR_DATA_"
        EV_DATA_.3: "EV_DATA_"
        BO_TX_BU_.3: "BO_TX_BU_"
        SG_MUL_VAL_.3: "SG_MUL_VAL_"
        SGTYPE_VAL_.3: "SGTYPE_VAL_"
        SGTYPE_.3: "SGTYPE_"
        BU_SG_REL_.3: "BU_SG_REL_"
        BU_BO_REL_.3: "BU_BO_REL_"
        BU_EV_REL_.3: "BU_EV_REL_"
        VAL_.3: "VAL_"
        EV_.3: "EV_"
        CM_.3: "CM_"
        VERSION.3: "VERSION"
        NS_.3: "NS_"
        BS_.3: "BS_"
        BU_.3: "BU_"
        BO_.3: "BO_"
        SG_.3: "SG_"

        NUMBER.2: /0[xX][0-9a-fA-F]+/ | /[0-9]+\.[0-9]*([eE][+-]?[0-9]+)?/ | /\.[0-9]+([eE][+-]?[0-9]+)?/ | /[0-9]+[eE][+-]?[0-9]+/ | /[0-9]+/
        STRING.2: /"([^"\\]|\\.|\"\")*"/
        IDENT: /[A-Za-z_][A-Za-z0-9_]*/

        %ignore /[ \t\r\n\f]+/
        %ignore /\/\/[^\n]*/
        %ignore /\/\*[\s\S]*?\*\//
        """;
}
