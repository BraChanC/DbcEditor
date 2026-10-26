/*
 * CANoe / Vector CANdb++ DBC grammar (ANTLR 4).
 * Covers the 35 segment keywords from Vector DBC File Format + SG_MUL_VAL_.
 */
grammar Dbc;

dbcFile
    : version? newSymbols? bitTiming? nodes? (message | comment | attributeDef | attributeDefault
      | attributeValue | baRel | valueTable | valueDescription | signalValueType | signalGroup
      | envVar | envVarData | boTxBu | sgMulVal
      | sgType | sgTypeVal | sigTypeRef | baDefSgType | baSgType | sigTypeValType
      | ignoredLine)* EOF
    ;

version
    : 'VERSION' STRING_LITERAL ';' ?
    ;

newSymbols
    : 'NS_' ':'? (IDENTIFIER | 'NS_DESC_' | 'CM_' | 'BA_DEF_' | 'BA_' | 'VAL_' | 'CAT_DEF_' | 'CAT_'
      | 'FILTER' | 'BA_DEF_DEF_' | 'EV_DATA_' | 'ENVVAR_DATA_' | 'SGTYPE_' | 'SGTYPE_VAL_'
      | 'BA_DEF_SGTYPE_' | 'BA_SGTYPE_' | 'SIG_TYPE_REF_' | 'VAL_TABLE_' | 'SIG_GROUP_'
      | 'SIG_VALTYPE_' | 'SIGTYPE_VALTYPE_' | 'BO_TX_BU_' | 'BA_DEF_REL_' | 'BA_REL_'
      | 'BA_DEF_DEF_REL_' | 'BU_SG_REL_' | 'BU_EV_REL_' | 'BU_BO_REL_' | 'SG_MUL_VAL_')*
    ;

bitTiming
    : 'BS_' ':' (INT (':' INT ',' INT)?)? ';' ?
    ;

nodes
    : 'BU_' ':' IDENTIFIER* ';' ?
    ;

message
    : 'BO_' INT IDENTIFIER ':' INT IDENTIFIER signal*
    ;

signal
    /* Use ('+'|'-') not SIGN — a bare '-' literal elsewhere (sgMulVal) steals '-' from SIGN. */
    : 'SG_' IDENTIFIER multiplexer? ':' INT '|' INT '@' INT sign
      '(' number ',' number ')' '[' number '|' number ']' STRING_LITERAL receiverList
    ;

sign
    : '+'
    | '-'
    ;

multiplexer
    : 'M'
    /* m<n> or extended-mux m<n>M (also multiplexor) */
    | 'm' INT 'M'?
    /* Lexer glues m0 / m1M into one IDENTIFIER when no spaces */
    | IDENTIFIER
    ;

receiverList
    : IDENTIFIER (',' IDENTIFIER)*
    ;

comment
    : 'CM_' STRING_LITERAL ';'
    | 'CM_' 'BU_' IDENTIFIER STRING_LITERAL ';'
    | 'CM_' 'BO_' INT STRING_LITERAL ';'
    | 'CM_' 'SG_' INT IDENTIFIER STRING_LITERAL ';'
    | 'CM_' 'EV_' IDENTIFIER STRING_LITERAL ';'
    ;

attributeDef
    : 'BA_DEF_' objectType? STRING_LITERAL attrValueType ';'
    | 'BA_DEF_REL_' objectType? STRING_LITERAL attrValueType ';'
    ;

objectType
    : 'BU_' | 'BO_' | 'SG_' | 'EV_' | 'BU_SG_REL_' | 'BU_BO_REL_' | 'BU_EV_REL_'
    ;

attrValueType
    : 'INT' number number
    | 'HEX' number number
    | 'FLOAT' number number
    | 'STRING'
    | 'ENUM' STRING_LITERAL (',' STRING_LITERAL)*
    ;

attributeDefault
    : 'BA_DEF_DEF_' STRING_LITERAL (STRING_LITERAL | number) ';'
    | 'BA_DEF_DEF_REL_' STRING_LITERAL (STRING_LITERAL | number) ';'
    ;

attributeValue
    : 'BA_' STRING_LITERAL (STRING_LITERAL | number) ';'
    | 'BA_' STRING_LITERAL 'BU_' IDENTIFIER (STRING_LITERAL | number) ';'
    | 'BA_' STRING_LITERAL 'BO_' INT (STRING_LITERAL | number) ';'
    | 'BA_' STRING_LITERAL 'SG_' INT IDENTIFIER (STRING_LITERAL | number) ';'
    | 'BA_' STRING_LITERAL 'EV_' IDENTIFIER (STRING_LITERAL | number) ';'
    ;

/* Relationship attribute assignment (cantools / CANdb++ forms) */
baRel
    : 'BA_REL_' STRING_LITERAL 'BU_SG_REL_' IDENTIFIER 'SG_' INT IDENTIFIER (STRING_LITERAL | number) ';'
    | 'BA_REL_' STRING_LITERAL 'BU_BO_REL_' IDENTIFIER INT (STRING_LITERAL | number) ';'
    | 'BA_REL_' STRING_LITERAL 'BU_EV_REL_' IDENTIFIER 'EV_'? IDENTIFIER (STRING_LITERAL | number) ';'
    ;

valueTable
    : 'VAL_TABLE_' IDENTIFIER (INT STRING_LITERAL)* ';'
    ;

valueDescription
    : 'VAL_' INT IDENTIFIER (INT STRING_LITERAL)* ';'
    | 'VAL_' 'EV_'? IDENTIFIER (INT STRING_LITERAL)* ';'
    ;

signalValueType
    /* With or without colon (CANdb++ / 2007) */
    : 'SIG_VALTYPE_' INT IDENTIFIER ':'? INT ';'
    ;

signalGroup
    : 'SIG_GROUP_' INT IDENTIFIER INT ':' IDENTIFIER* ';'
    ;

envVar
    : 'EV_' IDENTIFIER ':' INT '[' number '|' number ']' STRING_LITERAL number INT IDENTIFIER IDENTIFIER* ';'
    ;

envVarData
    /* EV_DATA_ accepted as ENVVAR_DATA_ alias */
    : ('ENVVAR_DATA_' | 'EV_DATA_') IDENTIFIER ':' INT ';'
    ;

boTxBu
    : 'BO_TX_BU_' INT ':' IDENTIFIER (',' IDENTIFIER)* ';'
    ;

sgMulVal
    : 'SG_MUL_VAL_' INT IDENTIFIER IDENTIFIER INT '-' INT (',' INT '-' INT)* ';'
    ;

/* Signal type template library */
sgType
    : 'SGTYPE_' IDENTIFIER ':' INT '@' INT sign
      '(' number ',' number ')' '[' number '|' number ']' STRING_LITERAL number ',' IDENTIFIER ';'
    ;

sgTypeVal
    : 'SGTYPE_VAL_' IDENTIFIER (INT STRING_LITERAL)* ';'
    ;

sigTypeRef
    : 'SIG_TYPE_REF_' INT IDENTIFIER ':' IDENTIFIER ';'
    ;

baDefSgType
    : 'BA_DEF_SGTYPE_' STRING_LITERAL attrValueType ';'
    ;

baSgType
    : 'BA_SGTYPE_' STRING_LITERAL IDENTIFIER (STRING_LITERAL | number) ';'
    ;

sigTypeValType
    : 'SIGTYPE_VALTYPE_' IDENTIFIER ':'? INT ';'
    ;

/* Catch-all so unknown / vendor / obsolete CAT_/FILTER lines don't kill the parse */
ignoredLine
    : ~('BO_' | 'SG_' | 'CM_' | 'BA_DEF_' | 'BA_DEF_DEF_' | 'BA_' | 'BA_REL_' | 'VAL_' | 'VAL_TABLE_'
      | 'SIG_VALTYPE_' | 'SIG_GROUP_' | 'EV_' | 'ENVVAR_DATA_' | 'EV_DATA_' | 'BO_TX_BU_' | 'SG_MUL_VAL_'
      | 'SGTYPE_' | 'SGTYPE_VAL_' | 'SIG_TYPE_REF_' | 'BA_DEF_SGTYPE_' | 'BA_SGTYPE_' | 'SIGTYPE_VALTYPE_'
      | 'VERSION' | 'NS_' | 'BS_' | 'BU_' | EOF)+ ';' ?
    ;

number
    /* '+' / '-' are parser literals (same tokens as sign / sgMulVal dash), not a SIGN lexer rule */
    : INT
    | FLOAT
    | '+' INT
    | '+' FLOAT
    | '-' INT
    | '-' FLOAT
    ;

INT
    : [0-9]+
    | '0' [xX] [0-9a-fA-F]+
    ;

/* J1939 / Vector often use 3.05E-005; also allow 1E-5 without a decimal point */
FLOAT
    : [0-9]+ '.' [0-9]* ([eE] [+-]? [0-9]+)?
    | [0-9]+ [eE] [+-]? [0-9]+
    | '.' [0-9]+ ([eE] [+-]? [0-9]+)?
    ;

STRING_LITERAL
    /* Vector uses ""; some J1939 exports also use \" */
    : '"' ( '\\' . | '""' | ~["\\\r\n] | '\r'? '\n' )* '"'
    ;

IDENTIFIER
    : [a-zA-Z_] [a-zA-Z0-9_]*
    ;

WS
    : [ \t\r\n]+ -> skip
    ;

LINE_COMMENT
    : '//' ~[\r\n]* -> skip
    ;
