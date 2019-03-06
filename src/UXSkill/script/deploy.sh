existingFunction=
updateConfig=


if [ "$1" == "update" ]; then
  updateConfig=true
elif [ "$1" == "" ]; then
  existingFunction=true
fi


# utility
function abs_path {
  echo $(cd $1;pwd)
}

funcName=UXSkill
zipName=UXSkill.zip
zipPath=../bin
region="us-east-1"
sourcePath=../bin/Debug/netcoreapp2.0/publish
role="arn:aws:iam::901211063728:role/uxSkill-dev-us-east-1-lambdaRole"
handler="UXSkill::UXSkill.Function::FunctionHandler"
timeout="300"

if [ -e $zipPath/$zipName ]; then
  echo "killing old file"
  rm $zipPath/$zipName
else
  echo "no old file"
fi

# get destination absolute path
absZipDest=$(abs_path $zipPath)

# save current location
currentPath=$(pwd)
cd $sourcePath

# package publish folder
zip -r $absZipDest/$zipName *

# restore old location
cd $currentPath

awsCreateFunction () {
echo "#### Creating New Function ######"
  aws lambda create-function \
  --region $region \
  --function-name $funcName \
  --zip-file fileb://$zipPath/$zipName \
  --role $role \
  --handler "${handler}" \
  --runtime dotnetcore2.0 \
  --timeout "${timeout}" \
  --description "transcription"

}

awsUpdateFunctionConfig () {
    echo "#### Updating Function Configuration ######"
    aws lambda update-function-configuration \
    --function-name $funcName \
    --handler "${handler}" \
    --timeout "${timeout}"
}

awsUpdateFunction () {
  echo "#### Updating Function ######"
  aws lambda update-function-code \
  --function-name $funcName \
  --zip-file fileb://$zipPath/$zipName \
  --publish
}

if [ -n "$updateConfig" ]; then
  awsUpdateFunctionConfig
elif [ -n "$existingFunction" ]; then
  awsUpdateFunction
else
  awsCreateFunction
fi
