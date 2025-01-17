grammar ScLang;

script
    : (topLevelStatement? EOL)* topLevelStatement?
    ;

topLevelStatement
    : K_SCRIPT_NAME identifier                                  #scriptNameStatement
    | K_SCRIPT_HASH integer                                     #scriptHashStatement

    | K_USING string                                            #usingStatement

    | K_PROC identifier parameterList EOL
      statementBlock
      K_ENDPROC                                                 #procedureStatement

    | K_FUNC returnType=identifier identifier parameterList EOL
      statementBlock
      K_ENDFUNC                                                 #functionStatement

    | K_PROTO K_PROC identifier parameterList                   #procedurePrototypeStatement
    | K_PROTO K_FUNC returnType=identifier identifier parameterList   #functionPrototypeStatement

    | K_NATIVE K_PROC identifier parameterList                   #procedureNativeStatement
    | K_NATIVE K_FUNC returnType=identifier identifier parameterList   #functionNativeStatement

    | K_STRUCT identifier EOL
      structFieldList
      K_ENDSTRUCT                                               #structStatement
    
    | K_CONST declaration                                       #constantVariableStatement
    | declaration                                               #staticVariableStatement

    | K_GLOBAL block=integer owner=identifier EOL
      (declaration? EOL)*
      K_ENDGLOBAL                                               #globalBlockStatement
    ;

statement
    : declaration                                               #variableDeclarationStatement
    | left=expression op=('=' | '*=' | '/=' | '%=' | '+=' | '-=' | '&=' | '^=' | '|=') right=expression   #assignmentStatement

    | K_IF condition=expression EOL
      thenBlock=statementBlock
      (K_ELSE EOL
      elseBlock=statementBlock)?
      K_ENDIF                                                   #ifStatement
    
    | K_WHILE condition=expression EOL
      statementBlock
      K_ENDWHILE                                                #whileStatement
    
    | K_REPEAT limit=expression counter=expression EOL
      statementBlock
      K_ENDREPEAT                                               #repeatStatement
    
    | K_SWITCH expression EOL
      switchCase*
      K_ENDSWITCH                                               #switchStatement

    | K_RETURN expression?                                      #returnStatement
    | expression argumentList                                   #invocationStatement
    ;

statementBlock
    : (statement? EOL)*
    ;

expression
    : '(' expression ')'                                            #parenthesizedExpression
    | expression '.' identifier                                     #memberAccessExpression
    | expression argumentList                                       #invocationExpression
    | expression arrayIndexer                                       #arrayAccessExpression
    | op=(K_NOT | '-') expression                                   #unaryExpression
    | left=expression op=('*' | '/' | '%') right=expression         #binaryExpression
    | left=expression op=('+' | '-') right=expression               #binaryExpression
    | left=expression op='&' right=expression                       #binaryExpression
    | left=expression op='^' right=expression                       #binaryExpression
    | left=expression op='|' right=expression                       #binaryExpression
    | left=expression op=('<' | '>' | '<=' | '>=') right=expression #binaryExpression
    | left=expression op=('==' | '<>') right=expression             #binaryExpression
    | left=expression op=K_AND right=expression                     #binaryExpression
    | left=expression op=K_OR right=expression                      #binaryExpression
    | '<<' expression ',' expression ',' expression '>>'            #vectorExpression
    | identifier                                                    #identifierExpression
    | (integer | float | string | bool)                             #literalExpression
    ;

switchCase
    : K_CASE value=expression EOL
      statementBlock                                                #valueSwitchCase
    | K_DEFAULT EOL
      statementBlock                                                #defaultSwitchCase
    ;

declaration
    : type=identifier initDeclaratorList
    ;

declarationNoInit
    : type=identifier declaratorList
    ;

singleDeclarationNoInit
    : type=identifier declarator
    ;
    
declaratorList
    : declarator (',' declarator)*
    ;

initDeclaratorList
    : initDeclarator (',' initDeclarator)*
    ;

initDeclarator
    : declarator ('=' initializer=expression)?
    ;

declarator
    : noRefDeclarator
    | refDeclarator
    ;

refDeclarator
    : '&' noRefDeclarator
    ;

noRefDeclarator
    : identifier                            #simpleDeclarator
    | noRefDeclarator '[' expression ']'    #arrayDeclarator
    | '(' refDeclarator ')'                 #parenthesizedRefDeclarator
    ;

structFieldList
    : (declarationNoInit? EOL)*
    ;

argumentList
    : '(' (expression (',' expression)*)? ')'
    ;

parameterList
    : '(' (singleDeclarationNoInit (',' singleDeclarationNoInit)*)? ')'
    ;

arrayIndexer
    : '[' expression ']'
    ;

identifier
    : IDENTIFIER
    ;

integer
    : DECIMAL_INTEGER
    | HEX_INTEGER
    ;

float
    : FLOAT
    ;

string
    : STRING
    ;

bool
    : K_TRUE
    | K_FALSE
    ; 

// keywords
K_PROC : P R O C;
K_ENDPROC : E N D P R O C;
K_FUNC : F U N C;
K_ENDFUNC : E N D F U N C;
K_STRUCT : S T R U C T;
K_ENDSTRUCT : E N D S T R U C T;
K_PROTO : P R O T O;
K_NATIVE : N A T I V E;
K_TRUE : T R U E;
K_FALSE : F A L S E;
K_NOT : N O T;
K_AND : A N D;
K_OR : O R;
K_IF : I F;
K_ELSE : E L S E;
K_ENDIF : E N D I F;
K_WHILE : W H I L E;
K_ENDWHILE : E N D W H I L E;
K_REPEAT : R E P E A T;
K_ENDREPEAT : E N D R E P E A T;
K_SWITCH : S W I T C H;
K_ENDSWITCH : E N D S W I T C H;
K_CASE : C A S E;
K_DEFAULT : D E F A U L T;
K_RETURN : R E T U R N;
K_SCRIPT_NAME : S C R I P T '_' N A M E;
K_SCRIPT_HASH : S C R I P T '_' H A S H;
K_USING : U S I N G;
K_CONST : C O N S T;
K_GLOBAL : G L O B A L;
K_ENDGLOBAL : E N D G L O B A L;

OP_ADD: '+';
OP_SUBTRACT: '-';
OP_MULTIPLY: '*';
OP_DIVIDE: '/';
OP_MODULO: '%';
OP_OR: '|';
OP_AND: '&';
OP_XOR: '^';
OP_EQUAL: '==';
OP_NOT_EQUAL: '<>';
OP_GREATER: '>';
OP_GREATER_OR_EQUAL: '>=';
OP_LESS: '<';
OP_LESS_OR_EQUAL: '<=';
OP_ASSIGN: '=';
OP_ASSIGN_ADD: '+=';
OP_ASSIGN_SUBTRACT: '-=';
OP_ASSIGN_MULTIPLY: '*=';
OP_ASSIGN_DIVIDE: '/=';
OP_ASSIGN_MODULO: '%=';
OP_ASSIGN_OR: '|=';
OP_ASSIGN_AND: '&=';
OP_ASSIGN_XOR: '^=';

IDENTIFIER
    :   [a-zA-Z_] [a-zA-Z_0-9]*
    ;

FLOAT
    : DECIMAL_INTEGER '.' DIGIT+
    ;

DECIMAL_INTEGER
    :   [-+]? DIGIT+
    ;

HEX_INTEGER
    :   '0x' HEX_DIGIT+
    ;

STRING
    : UNCLOSED_STRING '"'
    | UNCLOSED_STRING_SQ '\''
    ;

UNCLOSED_STRING
    : '"' (~["\\\r\n] | '\\' (. | EOF))*
    ;

UNCLOSED_STRING_SQ
    : '\'' (~['\\\r\n] | '\\' (. | EOF))*
    ;

COMMENT
    : '//' ~ [\r\n]* -> skip
    ;

EOL
    : [\r\n]+
    ;

WS
    : [ \t] -> skip
    ;

fragment DIGIT : [0-9];
fragment HEX_DIGIT : [0-9a-fA-F];

fragment A : [aA];
fragment B : [bB];
fragment C : [cC];
fragment D : [dD];
fragment E : [eE];
fragment F : [fF];
fragment G : [gG];
fragment H : [hH];
fragment I : [iI];
fragment J : [jJ];
fragment K : [kK];
fragment L : [lL];
fragment M : [mM];
fragment N : [nN];
fragment O : [oO];
fragment P : [pP];
fragment Q : [qQ];
fragment R : [rR];
fragment S : [sS];
fragment T : [tT];
fragment U : [uU];
fragment V : [vV];
fragment W : [wW];
fragment X : [xX];
fragment Y : [yY];
fragment Z : [zZ];